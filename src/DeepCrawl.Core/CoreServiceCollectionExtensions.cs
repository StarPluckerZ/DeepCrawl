using DeepCrawl.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DeepCrawl.Core;

public static class CoreServiceCollectionExtensions
{
    public static IServiceCollection AddDeepCrawlCore(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddScoped<ICrawlPipeline, CrawlPipeline>();

        var cacheStr = configuration["Search:CacheMinutes"] ?? "60";
        var searchOpts = new SearchServiceOptions { CacheMinutes = int.Parse(cacheStr) };
        services.AddSingleton(searchOpts);
        services.TryAddScoped<ISearchService, SearchService>();

        return services;
    }
}
