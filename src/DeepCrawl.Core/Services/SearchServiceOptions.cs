namespace DeepCrawl.Core.Services;

public class SearchServiceOptions
{
    public int CacheMinutes { get; set; } = 60;
    public int MaxResultCount { get; set; } = 50;
}
