namespace DeepCrawl.Domain.Enums;

public enum FetchTier
{
    ZhipuReader = 0,   // highest priority: Zhipu web reading API (markdown)
    HttpClient = 1,
    HttpClientProxy = 2,
    CloakBrowser = 3,
    CloakBrowserProxy = 4
}
