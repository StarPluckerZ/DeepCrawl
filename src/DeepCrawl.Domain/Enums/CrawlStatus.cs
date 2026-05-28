namespace DeepCrawl.Domain.Enums;

public enum CrawlStatus
{
    Pending = 0,
    Fetching = 1,
    Cleaning = 2,
    Completed = 3,
    Failed = 4
}
