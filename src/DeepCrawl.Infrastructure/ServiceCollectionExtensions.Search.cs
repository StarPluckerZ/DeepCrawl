using DeepCrawl.Domain.Abstractions;
using DeepCrawl.Domain.Models;
using DeepCrawl.Infrastructure.Search;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DeepCrawl.Infrastructure;

public static partial class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the search providers (Zhipu default, Bocha opt-in) and exposes the
    /// active one as <see cref="ISearchProvider"/> via the "Search:Provider" config key.
    /// </summary>
    private static IServiceCollection AddSearchProviders(
        this IServiceCollection services, IConfiguration configuration, ZhipuOptions zhipuOptions)
    {
        var selectedProvider = configuration["Search:Provider"] ?? "Zhipu";

        if (selectedProvider == "Zhipu" && string.IsNullOrWhiteSpace(zhipuOptions.ApiKey))
            Console.WriteLine("[WARN] ZHIPU_API_KEY env / Zhipu:ApiKey is empty — search will fail.");

        var bochaApiKey = Environment.GetEnvironmentVariable("BOCHA_API_KEY")
                          ?? configuration["Search:Bocha:ApiKey"] ?? "";
        if (selectedProvider == "Bocha" && string.IsNullOrWhiteSpace(bochaApiKey))
            Console.WriteLine("[WARN] BOCHA_API_KEY env / Search:Bocha:ApiKey is empty — search will fail.");

        services.AddHttpClient<ZhipuSearchProvider>(client =>
        {
            client.BaseAddress = new Uri(zhipuOptions.BaseUrl);
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {zhipuOptions.ApiKey}");
            client.Timeout = TimeSpan.FromSeconds(zhipuOptions.TimeoutSeconds);
        });
        services.AddHttpClient<BochaSearchProvider>(client =>
        {
            client.BaseAddress = new Uri(configuration["Search:Bocha:BaseUrl"] ?? "https://api.bocha.cn");
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {bochaApiKey}");
            client.Timeout = TimeSpan.FromSeconds(configuration.GetValue<int?>("Search:Bocha:TimeoutSeconds") ?? 30);
        });

        services.AddScoped<ISearchProvider>(sp => selectedProvider == "Zhipu"
            ? sp.GetRequiredService<ZhipuSearchProvider>()
            : sp.GetRequiredService<BochaSearchProvider>());

        return services;
    }
}
