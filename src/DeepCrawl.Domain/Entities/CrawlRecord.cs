using DeepCrawl.Domain.Enums;
using FreeSql.DataAnnotations;

namespace DeepCrawl.Domain.Entities;

[Table(Name = "crawl_records")]
public class CrawlRecord
{
    [Column(IsPrimary = true, IsIdentity = true)]
    public long Id { get; set; }

    [Column(StringLength = 2048, IsNullable = false)]
    public string Url { get; set; } = null!;

    [Column(StringLength = 64)]
    public string? HtmlHash { get; set; }

    [Column(StringLength = 64)]
    public string? ContextHash { get; set; }

    [Column(StringLength = -1)]
    public string? MarkdownContent { get; set; }

    [Column(StringLength = -1)]
    public string? CleanedMarkdown { get; set; }

    [Column(StringLength = -1)]
    public string? CleanedHtml { get; set; }

    [Column(StringLength = -1)]
    public string? MetadataJson { get; set; }

    [Column(StringLength = 32, IsNullable = false, MapType = typeof(string))]
    public CrawlStatus Status { get; set; } = CrawlStatus.Pending;

    [Column(StringLength = 64)]
    public string? ErrorCode { get; set; }

    [Column(StringLength = 1024)]
    public string? ErrorMessage { get; set; }

    [Column(CanUpdate = false, ServerTime = DateTimeKind.Local)]
    public DateTime CreatedAt { get; set; } 
    
    
    public DateTime? CompletedAt { get; set; }
    
    public DateTime LastAccessedAt { get; set; }
}

