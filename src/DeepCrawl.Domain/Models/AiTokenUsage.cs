namespace DeepCrawl.Domain.Models;

public class AiTokenUsage
{
    public string? Model { get; set; }
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public int? TotalTokens { get; set; }
    public int? ReasoningTokens { get; set; }
    public int? CacheHitTokens { get; set; }
    public int? CacheMissTokens { get; set; }
}
