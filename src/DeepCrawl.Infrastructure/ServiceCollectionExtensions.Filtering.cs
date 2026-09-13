using DeepCrawl.Domain.Abstractions;
using DeepCrawl.Infrastructure.Filtering;
using DeepCrawl.Infrastructure.Stats;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DeepCrawl.Infrastructure;

public static partial class ServiceCollectionExtensions
{
    /// <summary>Crawl-target filtering: dynamic domain reputation (adaptive blocking).</summary>
    private static IServiceCollection AddUrlFiltering(
        this IServiceCollection services, IConfiguration configuration)
    {
        var reputationOpts = configuration.GetSection("Reputation").Get<ReputationOptions>() ?? new ReputationOptions();
        services.AddSingleton(reputationOpts);
        services.AddScoped<DomainReputationService>();
        services.TryAddScoped<IDomainReporter>(sp => sp.GetRequiredService<DomainReputationService>());

        // Cache hit counting
        services.AddHostedService<CacheHitFlushService>();

        return services;
    }
}
