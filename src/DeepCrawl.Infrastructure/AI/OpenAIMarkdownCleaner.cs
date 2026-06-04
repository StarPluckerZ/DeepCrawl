using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using DeepCrawl.Domain.Abstractions;
using DeepCrawl.Domain.Models;

namespace DeepCrawl.Infrastructure.AI;

public class AIMarkdownCleanerOptions
{
    public string BaseUrl { get; set; } = "https://api.siliconflow.cn/v1/chat/completions";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "Qwen/Qwen3-8B";
    public string? ThinkingLevel { get; set; }
}

public class OpenAIMarkdownCleaner : IAIMarkdownCleaner
{
    private readonly IChatClient _chatClient;
    private readonly ILogger<OpenAIMarkdownCleaner> _logger;
    private readonly string? _thinkingLevel;
    private readonly string _model;

    private const string SystemPrompt = """
        你是网页内容清洗助手。接收网页 Markdown，只输出正文内容。
        ## 移除规则
        
        以下类型的内容一律移除：
        
        1. **行动引导**：登录、注册、下载App、订阅邮件、扫码、付款、无关正文的分享链接
        2. **互动统计**：点赞数、浏览数、评论条数、"赞同"计数、"人赞同"等
        3. **导航**："下一页"、"查看全部"、"展开全文"、"返回顶部"
        4. **作者板块**：头像图片链接、用户名链接等无用链接
        5. **发布信息**："发布于"、"更新于"、IP归属地（"·北京"、"·广东"等）
        6. **广告推广**：广告标记、赞助内容、推广链接、"热门推荐"、"大家都在搜"
        7. **页脚杂项**：版权声明、联系方式、条款链接、Cookie提示、备案信息、友站链接
        8. **无关图片**：无关、非正文的图片内容及正文中的装饰图片链接
        9. **无关推荐**: 无关正文的推荐内容，例如"热榜"、"热搜"、"相关推荐"、“阅读下一个”等
        10. **友联链接**：无关正文的网站链接
        
        ## 保留原则
        
        - 文章/问答的标题
        - 正文的所有段落、列表、表格、代码块
        - 正文中自然出现的与正文有关的链接
        
        ## 输出要求
        
        - 只输出清洗后的 Markdown，不加代码块标记
        - 不添加任何解释、说明或前后缀
        - 多篇内容之间用 --- 分隔
        """;

    public OpenAIMarkdownCleaner(IChatClient chatClient, ILogger<OpenAIMarkdownCleaner> logger, AIMarkdownCleanerOptions options)
    {
        _chatClient = chatClient;
        _logger = logger;
        _thinkingLevel = options.ThinkingLevel;
        _model = options.Model;
    }

    public async Task<(string Text, AiTokenUsage? Usage)> CleanAsync(string rawMarkdown, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawMarkdown))
            return (string.Empty, null);

        try
        {
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, SystemPrompt),
                new(ChatRole.User, rawMarkdown)
            };

            var options = new ChatOptions { Temperature = 0 };

            if (_thinkingLevel is { Length: > 0 } && _thinkingLevel != "none")
            {
                options.AdditionalProperties = new AdditionalPropertiesDictionary
                {
                    ["thinking"] = new { type = "enabled" },
                    ["reasoning_effort"] = _thinkingLevel
                };
            }

            var response = await _chatClient.GetResponseAsync(messages, options, ct);
            var output = (response.Text ?? string.Empty).Trim();

            AiTokenUsage? tokenUsage = null;
            if (response.Usage is { } u)
            {
                tokenUsage = new AiTokenUsage
                {
                    Model = _model,
                    PromptTokens = (int?)u.InputTokenCount,
                    CompletionTokens = (int?)u.OutputTokenCount,
                    TotalTokens = TryGetInt(u.AdditionalCounts, "total_tokens"),
                    CachedTokens = TryGetInt(u.AdditionalCounts, "cached_tokens"),
                    ReasoningTokens = TryGetInt(u.AdditionalCounts, "reasoning_tokens"),
                    CacheHitTokens = TryGetInt(u.AdditionalCounts, "prompt_cache_hit_tokens"),
                    CacheMissTokens = TryGetInt(u.AdditionalCounts, "prompt_cache_miss_tokens"),
                };
            }

            _logger.LogInformation("AI cleaning: {InputLen} -> {OutputLen} chars, {Tokens} tokens",
                rawMarkdown.Length, output.Length, tokenUsage?.TotalTokens);

            return (output, tokenUsage);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "AI cleaning failed, returning raw markdown");
            return (rawMarkdown, null);
        }
    }

    private static int? TryGetInt(AdditionalPropertiesDictionary<long>? dict, string key)
    {
        if (dict is not null && dict.TryGetValue(key, out var val))
            return (int)val;
        return null;
    }
}
