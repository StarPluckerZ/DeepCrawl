using DeepCrawl.Domain.Enums;
using FreeSql.DataAnnotations;

namespace DeepCrawl.Domain.Entities;

[Table(Name = "crawl_records")]
public class CrawlRecord
{
    [Column(IsPrimary = true, IsIdentity = true)]
    public long Id { get; set; }

    [Column(DbType = "varchar(2048)", IsNullable = false)]
    public string Url { get; set; } = null!;

    [Column(DbType = "varchar(64)")]
    public string? HtmlHash { get; set; }

    [Column(DbType = "varchar(64)")]
    public string? ContextHash { get; set; }

    [Column(DbType = "text")]
    public string? MarkdownContent { get; set; }

    [Column(DbType = "text")]
    public string? CleanedMarkdown { get; set; }

    [Column(DbType = "text")]
    public string? CleanedHtml { get; set; }

    [Column(DbType = "text")]
    public string? MetadataJson { get; set; }

    [Column(DbType = "varchar(32)", IsNullable = false)]
    public string Status { get; set; } = CrawlStatus.Pending.ToString();

    [Column(DbType = "varchar(64)")]
    public string? ErrorCode { get; set; }

    [Column(DbType = "varchar(1024)")]
    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public DateTime LastAccessedAt { get; set; } = DateTime.UtcNow;
}

