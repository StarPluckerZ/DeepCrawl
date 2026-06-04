using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeepSeekSDK;

public class DeepSeekClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly bool _ownsHttpClient;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public DeepSeekClient(string apiKey, string? baseUrl = null)
    {
        _apiKey = apiKey;
        _http = new HttpClient { BaseAddress = new Uri(baseUrl ?? "https://api.deepseek.com") };
        _http.Timeout = TimeSpan.FromSeconds(120);
        _ownsHttpClient = true;
    }

    public DeepSeekClient(HttpClient http, string apiKey)
    {
        _apiKey = apiKey;
        _http = http;
        _ownsHttpClient = false;
    }

    public void Dispose()
    {
        if (_ownsHttpClient) _http.Dispose();
    }

    public async Task<ChatCompletionResponse> ChatAsync(ChatCompletionRequest request, CancellationToken ct = default)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/chat/completions")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(request, JsonOptions),
                Encoding.UTF8,
                "application/json")
        };
        httpRequest.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_apiKey}");

        var response = await _http.SendAsync(httpRequest, HttpCompletionOption.ResponseContentRead, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new DeepSeekApiException((int)response.StatusCode, body);
        }

        var result = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(JsonOptions, ct);
        return result ?? throw new InvalidOperationException("Failed to deserialize DeepSeek response");
    }
}
