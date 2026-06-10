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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Polly;
using Polly.Extensions.Http;
using StackExchange.Redis;

namespace DeepCrawl.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDeepCrawlInfra(
        this IServiceCollection services, IConfiguration configuration)
    {
        // Crawl config
        var crawlConfig = configuration.GetSection("Crawl").Get<CrawlConfig>() ?? new CrawlConfig();
        crawlConfig.AiConfigured = !string.IsNullOrWhiteSpace(configuration["AI:ApiKey"]);
        services.AddSingleton(crawlConfig);

        // Redis
        var redisOptions = configuration.GetSection("Redis").Get<RedisOptions>() ?? new RedisOptions();
        services.AddSingleton(redisOptions);
        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect($"{redisOptions.Host}:{redisOptions.Port},password={redisOptions.Password}"));
        services.AddSingleton<IRedisClient, RedisClient>();

        // PostgreSQL + FreeSql
        var fsql = new FreeSqlBuilder()
            .UseConnectionString(DataType.PostgreSQL, configuration.GetConnectionString("PostgreSQL"))
            .UseAutoSyncStructure(true)
            .Build();

        var entityTypes = typeof(CrawlRecord).Assembly.GetTypes()
            .Where(t => Attribute.IsDefined(t, typeof(TableAttribute)));
        foreach (var type in entityTypes)
            fsql.CodeFirst.ConfigEntity(type, _ => { });

        services.AddSingleton<IFreeSql>(fsql);
        services.AddFreeRepository();

        // Generate API token if none exist
        var tokenCount = fsql.Select<ApiToken>().Count();
        if (tokenCount == 0)
        {
            var tokenBytes = RandomNumberGenerator.GetBytes(32);
            var token = "sk-" + Convert.ToHexStringLower(tokenBytes);
            fsql.Insert(new ApiToken { Token = token, IsActive = true }).ExecuteAffrows();
            Console.WriteLine($"[DeepCrawl] API token generated: {token}");
        }

        // CloakBrowser
        services.Configure<CloakBrowserClientOptions>(configuration.GetSection("CloakBrowser"));
        services.AddHttpClient<ICloakBrowserClient, CloakBrowserClient>(client =>
        {
            var baseUrl = configuration["CloakBrowser:BaseUrl"] ?? "http://localhost:8000";
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(120);
        })
        .AddPolicyHandler(HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(1, _ => TimeSpan.FromSeconds(2)));

        // Direct HTTP
        services.AddHttpClient("Direct", c =>
        {
            if (!string.IsNullOrWhiteSpace(crawlConfig.UserAgent))
                c.DefaultRequestHeaders.UserAgent.ParseAdd(crawlConfig.UserAgent);
            c.Timeout = TimeSpan.FromSeconds(15);
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = true });

        // Proxy HTTP
        if (crawlConfig.ProxyConfigured)
        {
            services.AddHttpClient("ProxyFetcher", c =>
            {
                if (!string.IsNullOrWhiteSpace(crawlConfig.UserAgent))
                    c.DefaultRequestHeaders.UserAgent.ParseAdd(crawlConfig.UserAgent);
                c.Timeout = TimeSpan.FromSeconds(15);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = true,
                Proxy = new WebProxy($"{crawlConfig.ProxyAddress}:{crawlConfig.ProxyPort}")
                {
                    Credentials = new NetworkCredential(crawlConfig.ProxyUsername, crawlConfig.ProxyPassword)
                },
                UseProxy = true
            });
        }

        services.AddSingleton<IDirectHttpFetcher, DirectHttpFetcher>();
        services.AddSingleton<TieredHttpFetcher>();

        // AI
        var endpoint = configuration["AI:BaseUrl"] ?? "https://api.siliconflow.cn/v1/chat/completions";
        var apiKey = configuration["AI:ApiKey"] ?? "";
        var model = configuration["AI:Model"] ?? "Qwen/Qwen3-8B";
        if (string.IsNullOrWhiteSpace(apiKey))
            Console.WriteLine("[WARN] AI:ApiKey is empty — AI cleaning will be skipped.");

        services.AddSingleton(new AIMarkdownCleanerOptions
        {
            BaseUrl = endpoint, ApiKey = apiKey, Model = model,
            ThinkingLevel = configuration["AI:ThinkingLevel"]
        });
        services.AddDeepSeekClient(apiKey, endpoint);

        // Clean pipeline
        services.AddSingleton<IContentAnalyzer, ContentAnalyzer>();
        services.AddSingleton<IHtmlCleaner, AngleSharpHtmlCleaner>();
        services.AddSingleton<IMarkdownConverter, ReverseMarkdownConverter>();
        services.AddSingleton<IAIMarkdownCleaner, OpenAIMarkdownCleaner>();
        services.AddSingleton<ICleanStep, DeepCrawl.Infrastructure.Cleaning.MetadataExtractorStep>();
        services.AddSingleton<ICleanStep, DeepCrawl.Infrastructure.Cleaning.AngleSharpHtmlCleanerStep>();
        services.AddSingleton<ICleanStep, DeepCrawl.Infrastructure.Cleaning.StripDataUriStep>();
        services.AddSingleton<ICleanStep, DeepCrawl.Infrastructure.Cleaning.ReverseMarkdownStep>();
        services.AddSingleton<ICleanStep, DeepCrawl.Infrastructure.Cleaning.OpenAICleanStep>();
        services.AddSingleton<CleanPipeline>();
        services.AddSingleton<ITokenValidator, TokenValidator>();

        // Reputation
        var reputationOpts = configuration.GetSection("Search:Reputation").Get<ReputationOptions>() ?? new ReputationOptions();
        services.AddSingleton(reputationOpts);
        services.AddScoped<DomainReputationService>();
        services.TryAddScoped<IUrlFilter>(sp => sp.GetRequiredService<DomainReputationService>());
        services.TryAddScoped<IDomainReporter>(sp => sp.GetRequiredService<DomainReputationService>());

        // UBlacklist
        var uBlacklistOpts = configuration.GetSection("Search:UBlacklist").Get<UBlacklistOptions>() ?? new UBlacklistOptions();
        services.AddSingleton(uBlacklistOpts);

        services.AddHttpClient("UBlacklist", c =>
        {
            c.Timeout = TimeSpan.FromSeconds(60);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("DeepCrawl/1.0");
        });
        services.AddSingleton<UBlacklistFilter>();
        services.TryAddSingleton<IUrlFilter>(sp => sp.GetRequiredService<UBlacklistFilter>());
        services.AddHostedService<UBlacklistUpdateService>();

        // Search provider
        var bochaApiKey = Environment.GetEnvironmentVariable("BOCHA_API_KEY")
                          ?? configuration["Search:Bocha:ApiKey"] ?? "";
        if (string.IsNullOrWhiteSpace(bochaApiKey))
            Console.WriteLine("[WARN] BOCHA_API_KEY env / Search:Bocha:ApiKey is empty — search will fail.");

        var bochaBaseUrl = configuration["Search:Bocha:BaseUrl"] ?? "https://api.bocha.cn";
        var bochaTimeout = configuration.GetValue<int?>("Search:Bocha:TimeoutSeconds") ?? 30;

        services.AddHttpClient<ISearchProvider, BochaSearchProvider>(client =>
        {
            client.BaseAddress = new Uri(bochaBaseUrl);
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {bochaApiKey}");
            client.Timeout = TimeSpan.FromSeconds(bochaTimeout);
        });

        return services;
    }
}
