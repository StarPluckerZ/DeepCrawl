using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DeepCrawl.Domain.Abstractions;

namespace DeepCrawl.Infrastructure.Clients;

public class CloakBrowserClientOptions
{
    public string BaseUrl { get; set; } = "http://localhost:8000";
    public int TimeoutSeconds { get; set; } = 60;
}

public class CloakBrowserClient : ICloakBrowserClient
{
    private readonly HttpClient _http;
    private readonly ILogger<CloakBrowserClient> _logger;

    public CloakBrowserClient(HttpClient http, IOptions<CloakBrowserClientOptions> options, ILogger<CloakBrowserClient> logger)
    {
        _http = http;
        _http.BaseAddress = new Uri(options.Value.BaseUrl);
        _logger = logger;
    }

    public async Task<string> FetchHtmlAsync(string url, string? waitUntil = null, string? proxy = null, CancellationToken ct = default)
    {
        _logger.LogInformation("Fetching HTML for {Url}", url);

        var payload = new { url, wait_until = waitUntil, proxy };
        var response = await _http.PostAsJsonAsync("/fetch", payload, JsonSerializerOptions.Default, ct);

        if (response.IsSuccessStatusCode)
        {
            var html = await response.Content.ReadAsStringAsync(ct);
            _logger.LogInformation("Fetched {Length} bytes for {Url}", html.Length, url);
            return html;
        }

        var errorJson = await response.Content.ReadAsStringAsync(ct);
        _logger.LogWarning("Fetch failed for {Url}: HTTP {Status} - {Body}", url, (int)response.StatusCode, errorJson);

        var errorCode = "FETCH_FAILED";
        try
        {
            var error = JsonDocument.Parse(errorJson);
            if (error.RootElement.TryGetProperty("code", out var code))
                errorCode = code.GetString() ?? "FETCH_FAILED";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse error JSON from cloak-service");
        }

        throw new CloakBrowserException(errorCode, errorJson);
    }
}
