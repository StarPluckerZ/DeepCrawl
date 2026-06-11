using DeepCrawl.Domain.Abstractions;
using Microsoft.Extensions.Logging;

namespace DeepCrawl.Infrastructure.Cleaning;

public class RobotsTxtService : IRobotsTxtService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<RobotsTxtService> _logger;

    public RobotsTxtService(
        IHttpClientFactory httpClientFactory,
        ILogger<RobotsTxtService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<string?> FetchAsync(string pageUrl, bool useProxy, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var uri))
            return null;

        var robotsUrl = $"{uri.Scheme}://{uri.Host}/robots.txt";
        var clientName = useProxy ? "ProxyFetcher" : "Direct";

        try
        {
            var client = _httpClientFactory.CreateClient(clientName);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            var response = await client.GetAsync(robotsUrl, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("robots.txt fetch returned {StatusCode} for {Url}", (int)response.StatusCode, robotsUrl);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(ct);
            _logger.LogDebug("robots.txt fetched for {Url} ({Length} bytes) via {Client}", robotsUrl, content.Length, clientName);
            return content;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogDebug("robots.txt fetch timed out for {Origin} via {Client}", uri.Host, clientName);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "robots.txt fetch failed for {Origin} via {Client}", uri.Host, clientName);
            return null;
        }
    }
}
