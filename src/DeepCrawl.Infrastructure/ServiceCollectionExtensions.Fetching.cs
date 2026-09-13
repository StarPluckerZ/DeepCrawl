using System.Net;
using DeepCrawl.Core.Services;
using DeepCrawl.Domain.Abstractions;
using DeepCrawl.Domain.Models;
using DeepCrawl.Infrastructure.Clients;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Extensions.Http;

namespace DeepCrawl.Infrastructure;

public static partial class ServiceCollectionExtensions
{
    /// <summary>
    /// The tiered fetch stack: Zhipu web reader (markdown), direct HttpClient,
    /// proxied HttpClient, CloakBrowser — each with its own named client and
    /// concurrency gate.
    /// </summary>
    private static IServiceCollection AddFetchers(
        this IServiceCollection services, IConfiguration configuration,
        CrawlConfig crawlConfig, ZhipuOptions zhipuOptions)
    {
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

        // Zhipu web reader (named client, DirectHttpFetcher pattern)
        services.AddHttpClient("Zhipu", c =>
        {
            c.BaseAddress = new Uri(zhipuOptions.BaseUrl);
            c.DefaultRequestHeaders.Add("Authorization", $"Bearer {zhipuOptions.ApiKey}");
            c.Timeout = TimeSpan.FromSeconds(zhipuOptions.TimeoutSeconds);
        });
        services.AddSingleton<IZhipuReaderFetcher, ZhipuReaderFetcher>();

        return services;
    }
}
