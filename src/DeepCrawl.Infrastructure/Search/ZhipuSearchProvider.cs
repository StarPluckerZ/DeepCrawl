using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DeepCrawl.Domain.Abstractions;
using DeepCrawl.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DeepCrawl.Infrastructure.Search;

/// <summary>
/// Zhipu (智谱) web search provider — POST /paas/v4/web_search (docs.bigmodel.cn).
/// Returns LLM-oriented results: title, summary content, link, site name.
/// </summary>
public class ZhipuSearchProvider(
    HttpClient httpClient,
    ZhipuOptions options,
    ILogger<ZhipuSearchProvider> logger) : ISearchProvider
{
    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public string ProviderName => "Zhipu";

    public async Task<List<SearchProviderResult>> SearchAsync(SearchProviderRequest request, CancellationToken ct = default)
    {
        // API constraint: search_query must be at most 70 characters
        var query = request.Query.Length > 70 ? request.Query[..70] : request.Query;

        var body = new ZhipuSearchBody
        {
            SearchQuery = query,
            SearchEngine = options.SearchEngine,
            Count = Math.Clamp(request.Count, 1, 50),
            // SearchService maps tbs to oneDay/oneWeek/oneMonth/oneYear/noLimit — pass through
            // directly; skip "cdr:"-style interval freshness values (contain "..") which
            // Zhipu does not accept
            SearchRecencyFilter = request.Freshness is { Length: > 0 } f && !f.Contains("..") ? f : null,
            ContentSize = request.Summary ? "high" : "medium",
            SearchDomainFilter = string.IsNullOrWhiteSpace(request.Include) ? null : request.Include
        };

        var content = new StringContent(JsonSerializer.Serialize(body, SerializeOptions), Encoding.UTF8, "application/json");

        logger.LogInformation("Zhipu search: {Query} count={Count} engine={Engine}", query, request.Count, options.SearchEngine);

        var httpResponse = await httpClient.PostAsync("paas/v4/web_search", content, ct);

        if (!httpResponse.IsSuccessStatusCode)
        {
            var errorBody = await httpResponse.Content.ReadAsStringAsync(ct);
            logger.LogError("Zhipu HTTP {Status}: {Body}", (int)httpResponse.StatusCode, errorBody);
            throw new HttpRequestException($"Search request failed with HTTP {(int)httpResponse.StatusCode}");
        }

        var responseJson = await httpResponse.Content.ReadAsStringAsync(ct);

        ZhipuSearchResponse? zhipuResponse;
        try
        {
            zhipuResponse = JsonSerializer.Deserialize<ZhipuSearchResponse>(responseJson, DeserializeOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse Zhipu response: {Response}", responseJson);
            throw new InvalidOperationException("Failed to parse search response", ex);
        }

        if (zhipuResponse?.Error is { } error)
        {
            logger.LogError("Zhipu API error: code={Code} message={Message}", error.Code, error.Message);
            throw new InvalidOperationException(error.Message ?? $"Zhipu search failed (code {error.Code})");
        }

        if (zhipuResponse?.SearchResult is null)
        {
            logger.LogError("Zhipu API returned no search_result: {Response}", responseJson);
            throw new InvalidOperationException("Zhipu search returned no results");
        }

        var results = new List<SearchProviderResult>(zhipuResponse.SearchResult.Count);
        foreach (var item in zhipuResponse.SearchResult)
        {
            if (!string.IsNullOrWhiteSpace(item.Link))
            {
                results.Add(new SearchProviderResult(
                    item.Title ?? "",
                    item.Link,
                    item.Content ?? ""
                ));
            }
        }

        return results;
    }
}

internal class ZhipuSearchBody
{
    [JsonPropertyName("search_query")]
    public string SearchQuery { get; set; } = null!;

    [JsonPropertyName("search_engine")]
    public string SearchEngine { get; set; } = "search_pro";

    [JsonPropertyName("search_intent")]
    public bool SearchIntent { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("search_recency_filter")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SearchRecencyFilter { get; set; }

    [JsonPropertyName("search_domain_filter")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SearchDomainFilter { get; set; }

    [JsonPropertyName("content_size")]
    public string ContentSize { get; set; } = "medium";
}

internal class ZhipuSearchResponse
{
    [JsonPropertyName("search_result")]
    public List<ZhipuSearchResultItem>? SearchResult { get; set; }

    [JsonPropertyName("error")]
    public ZhipuError? Error { get; set; }
}

internal class ZhipuSearchResultItem
{
    public string? Title { get; set; }
    public string? Content { get; set; }
    public string? Link { get; set; }
    public string? Media { get; set; }
    public string? Icon { get; set; }
    public string? Refer { get; set; }
    public string? PublishDate { get; set; }
}

internal class ZhipuError
{
    public int Code { get; set; }
    public string? Message { get; set; }
}
