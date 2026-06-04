using FreeSql.DataAnnotations;

namespace DeepCrawl.Domain.Entities;

[Table(Name = "crawl_statistics")]
public class CrawlStatistic
{
    [Column(IsPrimary = true, IsIdentity = true)]
    public long Id { get; set; }

    public long CrawlRecordId { get; set; }

    [Navigate(nameof(CrawlRecordId))]
    public CrawlRecord? Record { get; set; }

    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public int? TotalTokens { get; set; }
    public int? ReasoningTokens { get; set; }
    public int? CacheHitTokens { get; set; }
    public int? CacheMissTokens { get; set; }

    [Column(StringLength = 64)]
    public string? Model { get; set; }
    
    [Column(CanUpdate = false, ServerTime = DateTimeKind.Local)]
    public DateTime CreatedAt { get; set; }
}
