namespace DeepCrawl.Domain.Models;

/// <summary>
/// Options for the Zhipu (智谱) tool APIs — web search and web reading share the same
/// base URL and API key (Bearer auth). Bound from the "Zhipu" config section.
/// </summary>
public class ZhipuOptions
{
    /// <summary>API base URL, including the "/api" path segment (e.g. https://open.bigmodel.cn/api).</summary>
    public string BaseUrl { get; set; } = "https://open.bigmodel.cn/api";

    public string? ApiKey { get; set; }

    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Web search engine: search_std | search_pro | search_pro_sogou | search_pro_quark.</summary>
    public string SearchEngine { get; set; } = "search_pro";
}
