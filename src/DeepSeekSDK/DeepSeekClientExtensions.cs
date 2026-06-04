using Microsoft.Extensions.DependencyInjection;

namespace DeepSeekSDK;

public static class DeepSeekClientExtensions
{
    public static IServiceCollection AddDeepSeekClient(
        this IServiceCollection services, string apiKey, string? baseUrl = null)
    {
        services.AddHttpClient("DeepSeekClient", client =>
        {
            client.BaseAddress = new Uri(baseUrl ?? "https://api.deepseek.com");
            client.Timeout = TimeSpan.FromSeconds(120);
        });

        services.AddSingleton(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            return new DeepSeekClient(factory.CreateClient("DeepSeekClient"), apiKey);
        });

        return services;
    }
}
