using DeepCrawl.Domain.Abstractions;
using Microsoft.Extensions.Logging;

namespace DeepCrawl.Infrastructure.Clients;

public class DirectHttpFetcher : IDirectHttpFetcher
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DirectHttpFetcher> _logger;

    public DirectHttpFetcher(IHttpClientFactory httpClientFactory, ILogger<DirectHttpFetcher> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<string> FetchDirectAsync(string url, CancellationToken ct = default)
    {
        _logger.LogDebug("FetchDirect: {Url}", url);
        using var http = _httpClientFactory.CreateClient("Direct");
        return await http.GetStringAsync(url, ct);
    }

    public async Task<string> FetchWithProxyAsync(string url, CancellationToken ct = default)
    {
        _logger.LogDebug("FetchWithProxy: {Url}", url);
        using var http = _httpClientFactory.CreateClient("ProxyFetcher");
        return await http.GetStringAsync(url, ct);
    }
}
