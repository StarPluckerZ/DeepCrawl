using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DeepCrawl.Domain.Abstractions;
using DeepCrawl.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DeepCrawl.Infrastructure.Search;

public class BochaSearchProvider : ISearchProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BochaSearchProvider> _logger;

    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public string ProviderName => "Bocha";

    public BochaSearchProvider(HttpClient httpClient, ILogger<BochaSearchProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<List<SearchProviderResult>> SearchAsync(SearchProviderRequest request, CancellationToken ct = default)
    {
        var body = new BochaSearchBody
        {
            Query = request.Query,
            Count = request.Count,
            Freshness = request.Freshness ?? "noLimit",
            Summary = request.Summary,
            Include = request.Include,
            Exclude = request.Exclude
        };

        var content = new StringContent(JsonSerializer.Serialize(body, SerializeOptions), Encoding.UTF8, "application/json");

        _logger.LogInformation("Bocha search: {Query} count={Count}", request.Query, request.Count);

        var httpResponse = await _httpClient.PostAsync("/v1/web-search", content, ct);

        if (!httpResponse.IsSuccessStatusCode)
        {
            var errorBody = await httpResponse.Content.ReadAsStringAsync(ct);
            _logger.LogError("Bocha HTTP {Status}: {Body}", (int)httpResponse.StatusCode, errorBody);
            throw new HttpRequestException($"Search request failed with HTTP {(int)httpResponse.StatusCode}");
        }

        var responseJson = await httpResponse.Content.ReadAsStringAsync(ct);

        BochaSearchResponse? bochaResponse;
        try
        {
            bochaResponse = JsonSerializer.Deserialize<BochaSearchResponse>(responseJson, DeserializeOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse Bocha response: {Response}", responseJson);
            throw new InvalidOperationException("Failed to parse search response", ex);
        }

        if (bochaResponse is not { Code: 200 } || bochaResponse.Data?.WebPages?.Value is null)
        {
            _logger.LogError("Bocha API error: code={Code} msg={Msg}", bochaResponse?.Code, bochaResponse?.Msg);
            throw new InvalidOperationException(bochaResponse?.Msg ?? "Search request failed");
        }

        var results = new List<SearchProviderResult>(bochaResponse.Data.WebPages.Value.Count);
        foreach (var page in bochaResponse.Data.WebPages.Value)
        {
            if (!string.IsNullOrWhiteSpace(page.Url))
            {
                results.Add(new SearchProviderResult(
                    page.Name ?? "",
                    page.Url,
                    page.Snippet ?? ""
                ));
            }
        }

        return results;
    }
}

internal class BochaSearchBody
{
    [JsonPropertyName("query")]
    public string Query { get; set; } = null!;

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("freshness")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Freshness { get; set; }

    [JsonPropertyName("summary")]
    public bool Summary { get; set; }

    [JsonPropertyName("include")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Include { get; set; }

    [JsonPropertyName("exclude")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Exclude { get; set; }
}

internal class BochaSearchResponse
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("msg")]
    public string? Msg { get; set; }

    [JsonPropertyName("data")]
    public BochaSearchData? Data { get; set; }
}

internal class BochaSearchData
{
    [JsonPropertyName("webPages")]
    public BochaWebPages? WebPages { get; set; }
}

internal class BochaWebPages
{
    [JsonPropertyName("value")]
    public List<BochaWebPageValue>? Value { get; set; }
}

internal class BochaWebPageValue
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("snippet")]
    public string? Snippet { get; set; }
}
