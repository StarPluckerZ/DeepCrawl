using DeepCrawl.Domain.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DeepCrawl.Infrastructure;

/// <summary>
/// Composition root for all DeepCrawl infrastructure services.
/// Each concern (data stores, fetchers, cleaning, filtering, search) lives in
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

        // Zhipu — search and web reader share the key; the reader tier is
        // disabled when the key is missing (mirrors ProxyConfigured)
        var zhipuOptions = ResolveZhipuOptions(configuration);
        crawlConfig.ZhipuReaderConfigured = !string.IsNullOrWhiteSpace(zhipuOptions.ApiKey);
        services.AddSingleton(zhipuOptions);

        services.AddDataStores(configuration);
        services.AddFetchers(configuration, crawlConfig, zhipuOptions);
        services.AddCleaning(configuration);
        services.AddUrlFiltering(configuration);
        services.AddSearchProviders(configuration, zhipuOptions);

        return services;
    }

    /// <summary>
    /// Binds the "Zhipu" section, letting the ZHIPU_API_KEY environment variable
    /// override the configured key (search and reader use the same credential).
    /// </summary>
    private static ZhipuOptions ResolveZhipuOptions(IConfiguration configuration)
    {
        var options = configuration.GetSection("Zhipu").Get<ZhipuOptions>() ?? new ZhipuOptions();
        var apiKey = Environment.GetEnvironmentVariable("ZHIPU_API_KEY")
                     ?? configuration["Zhipu:ApiKey"] ?? "";
        if (!string.IsNullOrWhiteSpace(apiKey))
            options.ApiKey = apiKey;
        return options;
    }
}
