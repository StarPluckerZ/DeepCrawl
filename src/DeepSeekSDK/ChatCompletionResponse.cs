using System.Text.Json.Serialization;

namespace DeepSeekSDK;

public class ChatCompletionResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("choices")]
    public List<ChatChoice> Choices { get; set; } = [];

    [JsonPropertyName("created")]
    public long Created { get; set; }

    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("usage")]
    public Usage? Usage { get; set; }

    public string? GetContent() => Choices.FirstOrDefault()?.Message?.Content;
    public string? GetReasoning() => Choices.FirstOrDefault()?.Message?.ReasoningContent;
}
