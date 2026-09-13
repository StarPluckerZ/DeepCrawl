using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DeepCrawl.Domain.Abstractions;
using Microsoft.Extensions.Logging;

namespace DeepCrawl.Infrastructure.Clients;

/// <summary>
/// Zhipu web reading client — POST /paas/v4/reader (docs.bigmodel.cn).
/// Returns the page's parsed main content as markdown, or null on any failure
/// so the caller can fall back to lower fetch tiers.
/// </summary>
public class ZhipuReaderFetcher(
    IHttpClientFactory httpClientFactory,
    ILogger<ZhipuReaderFetcher> logger) : IZhipuReaderFetcher
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

    public async Task<string?> ReadAsync(string url, CancellationToken ct = default)
    {
        var body = new ZhipuReaderBody
        {
            Url = url,
            ReturnFormat = "markdown",
            RetainImages = false
        };

        var content = new StringContent(JsonSerializer.Serialize(body, SerializeOptions), Encoding.UTF8, "application/json");

        using var http = httpClientFactory.CreateClient("Zhipu");
        HttpResponseMessage httpResponse;
        try
        {
            httpResponse = await http.PostAsync("paas/v4/reader", content, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Zhipu reader request failed for {Url}", url);
            return null;
        }

        if (!httpResponse.IsSuccessStatusCode)
        {
            var errorBody = await httpResponse.Content.ReadAsStringAsync(ct);
            logger.LogWarning("Zhipu reader HTTP {Status} for {Url}: {Body}", (int)httpResponse.StatusCode, url, errorBody);
            return null;
        }

        var responseJson = await httpResponse.Content.ReadAsStringAsync(ct);

        ZhipuReaderResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<ZhipuReaderResponse>(responseJson, DeserializeOptions);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse Zhipu reader response for {Url}: {Response}", url, responseJson);
            return null;
        }

        if (response?.Error is { } error)
        {
            logger.LogWarning("Zhipu reader API error for {Url}: code={Code} message={Message}", url, error.Code, error.Message);
            return null;
        }

        if (string.IsNullOrWhiteSpace(response?.ReaderResult?.Content))
        {
            logger.LogWarning("Zhipu reader returned empty content for {Url}", url);
            return null;
        }

        return response.ReaderResult.Content;
    }
}

internal class ZhipuReaderBody
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = null!;

    [JsonPropertyName("return_format")]
    public string ReturnFormat { get; set; } = "markdown";

    [JsonPropertyName("retain_images")]
    public bool RetainImages { get; set; }
}

internal class ZhipuReaderResponse
{
    [JsonPropertyName("reader_result")]
    public ZhipuReaderResult? ReaderResult { get; set; }

    [JsonPropertyName("error")]
    public ZhipuReaderError? Error { get; set; }
}

internal class ZhipuReaderResult
{
    public string? Content { get; set; }
    public string? Description { get; set; }
    public string? Title { get; set; }
    public string? Url { get; set; }
}

internal class ZhipuReaderError
{
    public int Code { get; set; }
    public string? Message { get; set; }
}
