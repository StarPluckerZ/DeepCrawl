namespace DeepCrawl.Domain.Models;

public class CrawlConfig
{
    public string? ProxyAddress { get; set; }
    public int ProxyPort { get; set; }
    public string? ProxyUsername { get; set; }
    public string? ProxyPassword { get; set; }
    public string? UserAgent { get; set; }
    public bool AiConfigured { get; set; }
    public int MinTextLength { get; set; } = 200;

    public bool ProxyConfigured => !string.IsNullOrWhiteSpace(ProxyAddress);
    public string ProxyUrl => $"http://{ProxyUsername}:{ProxyPassword}@{ProxyAddress}:{ProxyPort}";
}
