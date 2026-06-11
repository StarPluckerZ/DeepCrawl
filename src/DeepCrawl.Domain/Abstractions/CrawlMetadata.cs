namespace DeepCrawl.Domain.Abstractions;

public class CrawlMetadata
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Language { get; set; }
    public string? Keywords { get; set; }
    public string? Robots { get; set; }
    public string? RobotsTxt { get; set; }
    public string? OgTitle { get; set; }
    public string? OgDescription { get; set; }
    public string? OgUrl { get; set; }
    public string? OgImage { get; set; }
    public string? OgLocaleAlternate { get; set; }
    public string? OgSiteName { get; set; }
    public string? SourceURL { get; set; }
    public int? StatusCode { get; set; }
    public string? ContentType { get; set; }
}
