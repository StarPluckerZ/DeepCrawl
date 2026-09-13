using DeepCrawl.Domain.Abstractions;
using DeepCrawl.Infrastructure.Filtering;
using DeepCrawl.Infrastructure.Stats;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DeepCrawl.Infrastructure;

public static partial class ServiceCollectionExtensions
{
    /// <summary>Search-result and crawl-target filtering: domain reputation + uBlacklist.</summary>
    private static IServiceCollection AddUrlFiltering(
        this IServiceCollection services, IConfiguration configuration)
    {
        // Reputation
        var reputationOpts = configuration.GetSection("Search:Reputation").Get<ReputationOptions>() ?? new ReputationOptions();
        services.AddSingleton(reputationOpts);
        services.AddScoped<DomainReputationService>();
        services.TryAddScoped<IUrlFilter>(sp => sp.GetRequiredService<DomainReputationService>());
        services.TryAddScoped<IDomainReporter>(sp => sp.GetRequiredService<DomainReputationService>());

        // UBlacklist
        var uBlacklistOpts = configuration.GetSection("Search:UBlacklist").Get<UBlacklistOptions>() ?? new UBlacklistOptions();

        var file = Path.Combine(AppContext.BaseDirectory, "UBlacklistSubscription.txt");
        if (File.Exists(file))
        {
            var fileUrls = File.ReadAllLines(file)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith('#'));
            uBlacklistOpts.SubscriptionUrls.AddRange(fileUrls);
        }
        uBlacklistOpts.SubscriptionUrls = uBlacklistOpts.SubscriptionUrls.Distinct().ToList();

        services.AddSingleton(uBlacklistOpts);

        services.AddHttpClient("UBlacklist", c =>
        {
            c.Timeout = TimeSpan.FromSeconds(60);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("DeepCrawl/1.0");
        });
        services.AddSingleton<UBlacklistFilter>();
        services.TryAddSingleton<IUrlFilter>(sp => sp.GetRequiredService<UBlacklistFilter>());
        services.AddHostedService<UBlacklistUpdateService>();

        // Cache hit counting
        services.AddHostedService<CacheHitFlushService>();

        return services;
    }
}
