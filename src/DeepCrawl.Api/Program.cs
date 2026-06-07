using System.Net;
using System.Security.Cryptography;
using DeepCrawl.Core.Services;
using DeepCrawl.Domain.Abstractions;
using DeepCrawl.Domain.Entities;
using DeepCrawl.Domain.Models;
using DeepCrawl.Infrastructure.AI;
using DeepCrawl.Infrastructure.Auth;
using DeepCrawl.Infrastructure.Cleaning;
using DeepCrawl.Infrastructure.Caching;
using DeepCrawl.Infrastructure.Clients;
using DeepCrawl.Infrastructure.Filtering;
using DeepCrawl.Infrastructure.Search;
using DeepSeekSDK;
using FreeSql;
using FreeSql.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Polly;
using Polly.Extensions.Http;
using Scalar.AspNetCore;
using Serilog;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, lc) => lc.ReadFrom.Configuration(ctx.Configuration));

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddAuthentication("ApiToken")
    .AddScheme<AuthenticationSchemeOptions, ApiTokenAuthenticationHandler>("ApiToken", null);
builder.Services.AddAuthorization();

builder.Services.Configure<CloakBrowserClientOptions>(builder.Configuration.GetSection("CloakBrowser"));
builder.Services.Configure<AIMarkdownCleanerOptions>(builder.Configuration.GetSection("AI"));

var crawlConfig = builder.Configuration.GetSection("Crawl").Get<CrawlConfig>() ?? new CrawlConfig();
crawlConfig.AiConfigured = !string.IsNullOrWhiteSpace(builder.Configuration["AI:ApiKey"]);
builder.Services.AddSingleton(crawlConfig);

var redisOptions = builder.Configuration.GetSection("Redis").Get<RedisOptions>() ?? new RedisOptions();
builder.Services.AddSingleton(redisOptions);
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect($"{redisOptions.Host}:{redisOptions.Port},password={redisOptions.Password}"));
builder.Services.AddSingleton<IRedisClient, RedisClient>();

var fsql = new FreeSqlBuilder()
    .UseConnectionString(DataType.PostgreSQL, builder.Configuration.GetConnectionString("PostgreSQL"))
    .UseAutoSyncStructure(true)
    .Build();

var entityTypes = typeof(CrawlRecord).Assembly.GetTypes()
    .Where(t => Attribute.IsDefined(t, typeof(TableAttribute)));

foreach (var type in entityTypes)
    fsql.CodeFirst.ConfigEntity(type, _ => { });

builder.Services.AddSingleton<IFreeSql>(fsql);
builder.Services.AddFreeRepository();

// Generate initial API token if none exist
var tokenCount = fsql.Select<ApiToken>().Count();
if (tokenCount == 0)
{
    var tokenBytes = RandomNumberGenerator.GetBytes(32);
    var token = "sk-" + Convert.ToHexStringLower(tokenBytes);
    fsql.Insert(new ApiToken { Token = token, IsActive = true }).ExecuteAffrows();

    Console.WriteLine();
    Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
    Console.WriteLine("║         DEEPCRAWL API TOKEN                              ║");
    Console.WriteLine("║                                                          ║");
    Console.WriteLine("║  No tokens found — a new token has been generated:        ║");
    Console.WriteLine($"║           {token}              ║");
    Console.WriteLine("║                                                          ║");
    Console.WriteLine("║  Save this token. It will NOT be printed again.           ║");
    Console.WriteLine("║  Use it in the Authorization header:                     ║");
    Console.WriteLine("║      Authorization: Bearer {0}  ║", token);
    Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
    Console.WriteLine();
}


builder.Services.AddHttpClient<ICloakBrowserClient, CloakBrowserClient>(client =>
{
    var baseUrl = builder.Configuration["CloakBrowser:BaseUrl"] ?? "http://localhost:8000";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(120);
})
.AddPolicyHandler(HttpPolicyExtensions
    .HandleTransientHttpError()
    .WaitAndRetryAsync(1, _ => TimeSpan.FromSeconds(2)));

builder.Services.AddHttpClient("Direct", c =>
{
    if (!string.IsNullOrWhiteSpace(crawlConfig.UserAgent))
        c.DefaultRequestHeaders.UserAgent.ParseAdd(crawlConfig.UserAgent);
    c.Timeout = TimeSpan.FromSeconds(15);
});

if (crawlConfig.ProxyConfigured)
{
    builder.Services.AddHttpClient("ProxyFetcher", c =>
    {
        if (!string.IsNullOrWhiteSpace(crawlConfig.UserAgent))
            c.DefaultRequestHeaders.UserAgent.ParseAdd(crawlConfig.UserAgent);
        c.Timeout = TimeSpan.FromSeconds(15);
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        Proxy = new WebProxy($"{crawlConfig.ProxyAddress}:{crawlConfig.ProxyPort}")
        {
            Credentials = new NetworkCredential(crawlConfig.ProxyUsername, crawlConfig.ProxyPassword)
        },
        UseProxy = true
    });
}

builder.Services.AddSingleton<IDirectHttpFetcher, DirectHttpFetcher>();
builder.Services.AddSingleton<TieredHttpFetcher>();

var endpoint = builder.Configuration["AI:BaseUrl"] ?? "https://api.siliconflow.cn/v1/chat/completions";
var apiKey = builder.Configuration["AI:ApiKey"] ?? "";
var model = builder.Configuration["AI:Model"] ?? "Qwen/Qwen3-8B";
var thinkingLevel = builder.Configuration["AI:ThinkingLevel"];

if (string.IsNullOrWhiteSpace(apiKey))
    Console.WriteLine("[WARN] AI:ApiKey is empty — AI cleaning will be skipped.");

builder.Services.AddSingleton(new AIMarkdownCleanerOptions
{
    BaseUrl = endpoint,
    ApiKey = apiKey,
    Model = model,
    ThinkingLevel = thinkingLevel
});

builder.Services.AddDeepSeekClient(apiKey, endpoint);

builder.Services.AddSingleton<IContentAnalyzer, ContentAnalyzer>();
builder.Services.AddSingleton<IHtmlCleaner, AngleSharpHtmlCleaner>();
builder.Services.AddSingleton<IMarkdownConverter, ReverseMarkdownConverter>();
builder.Services.AddSingleton<IAIMarkdownCleaner, OpenAIMarkdownCleaner>();

builder.Services.AddSingleton<ICleanStep, DeepCrawl.Infrastructure.Cleaning.MetadataExtractorStep>();
builder.Services.AddSingleton<ICleanStep, DeepCrawl.Infrastructure.Cleaning.AngleSharpHtmlCleanerStep>();
builder.Services.AddSingleton<ICleanStep, DeepCrawl.Infrastructure.Cleaning.StripDataUriStep>();
builder.Services.AddSingleton<ICleanStep, DeepCrawl.Infrastructure.Cleaning.ReverseMarkdownStep>();
builder.Services.AddSingleton<ICleanStep, DeepCrawl.Infrastructure.Cleaning.OpenAICleanStep>();
builder.Services.AddSingleton<CleanPipeline>();
builder.Services.AddSingleton<ITokenValidator, TokenValidator>();
builder.Services.AddScoped<ICrawlPipeline, CrawlPipeline>();

// --- Search ---
var bochaApiKey = Environment.GetEnvironmentVariable("BOCHA_API_KEY")
                  ?? builder.Configuration["Search:Bocha:ApiKey"] ?? "";

if (string.IsNullOrWhiteSpace(bochaApiKey))
    Console.WriteLine("[WARN] BOCHA_API_KEY env / Search:Bocha:ApiKey is empty — search will fail.");

var bochaBaseUrl = builder.Configuration["Search:Bocha:BaseUrl"] ?? "https://api.bocha.cn";
var bochaTimeout = builder.Configuration.GetValue<int?>("Search:Bocha:TimeoutSeconds") ?? 30;

builder.Services.AddHttpClient<ISearchProvider, BochaSearchProvider>(client =>
{
    client.BaseAddress = new Uri(bochaBaseUrl);
    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {bochaApiKey}");
    client.Timeout = TimeSpan.FromSeconds(bochaTimeout);
});

var uBlacklistOpts = builder.Configuration.GetSection("Search:UBlacklist").Get<UBlacklistOptions>() ?? new UBlacklistOptions();
builder.Services.AddSingleton(uBlacklistOpts);

builder.Services.AddHttpClient("UBlacklist", c =>
{
    c.Timeout = TimeSpan.FromSeconds(60);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("DeepCrawl/1.0");
});

builder.Services.AddSingleton<UBlacklistFilter>();
builder.Services.AddSingleton<IUrlFilter>(sp => sp.GetRequiredService<UBlacklistFilter>());

var reputationOpts = builder.Configuration.GetSection("Search:Reputation").Get<ReputationOptions>() ?? new ReputationOptions();
builder.Services.AddSingleton(reputationOpts);

builder.Services.AddScoped<DomainReputationService>();
builder.Services.AddScoped<IUrlFilter>(sp => sp.GetRequiredService<DomainReputationService>());
builder.Services.AddScoped<IDomainReporter>(sp => sp.GetRequiredService<DomainReputationService>());

builder.Services.AddHostedService<UBlacklistUpdateService>();

var searchServiceOpts = new SearchServiceOptions
{
    CacheMinutes = builder.Configuration.GetValue<int?>("Search:CacheMinutes") ?? 60
};
builder.Services.AddSingleton(searchServiceOpts);
builder.Services.AddScoped<ISearchService, SearchService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
