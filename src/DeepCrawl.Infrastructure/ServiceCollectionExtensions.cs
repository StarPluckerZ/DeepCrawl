using DeepCrawl.Domain.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DeepCrawl.Infrastructure;

/// <summary>
/// Composition root for all DeepCrawl infrastructure services.
/// Each concern (data stores, fetchers, cleaning, filtering) lives in
/// its own partial file; this entry point resolves shared options and wires
/// them together.
/// </summary>
public static partial class ServiceCollectionExtensions
{
    public static IServiceCollection AddDeepCrawlInfra(
        this IServiceCollection services, IConfiguration configuration)
    {
        // Crawl config
        var crawlConfig = configuration.GetSection("Crawl").Get<CrawlConfig>() ?? new CrawlConfig();
        crawlConfig.AiConfigured = !string.IsNullOrWhiteSpace(configuration["AI:ApiKey"]);
        services.AddSingleton(crawlConfig);

        services.AddDataStores(configuration);
        services.AddFetchers(configuration, crawlConfig);
        services.AddCleaning(configuration);
        services.AddUrlFiltering(configuration);

        return services;
    }
}
