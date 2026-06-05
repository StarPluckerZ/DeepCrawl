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
    public int CacheBaseMinutes { get; set; } = 60;
    public int CacheMaxMinutes { get; set; } = 10080;
    public int CacheThreshold { get; set; } = 3;
    public int CacheResetDivisor { get; set; } = 5;
    public int CacheResetCap { get; set; } = 6;

    public bool ProxyConfigured => !string.IsNullOrWhiteSpace(ProxyAddress);
    public string ProxyUrl => $"http://{Uri.EscapeDataString(ProxyUsername ?? "")}:{Uri.EscapeDataString(ProxyPassword ?? "")}@{ProxyAddress}:{ProxyPort}";
}
