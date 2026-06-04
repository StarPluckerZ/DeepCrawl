using System.Security.Cryptography;
using System.Text;
using DeepCrawl.Domain.Abstractions;
using DeepCrawl.Domain.Enums;
using DeepCrawl.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DeepCrawl.Infrastructure.Cleaning;

public class OpenAICleanStep : ICleanStep
{
    private readonly IAIMarkdownCleaner _aiCleaner;
    private readonly IRedisClient _redis;
    private readonly ILogger<OpenAICleanStep> _logger;

    public OpenAICleanStep(IAIMarkdownCleaner aiCleaner, IRedisClient redis, ILogger<OpenAICleanStep> logger)
    {
        _aiCleaner = aiCleaner;
        _redis = redis;
        _logger = logger;
    }

    public CleanStage Stage => CleanStage.Markdown;
    public int Order => 20;

    public async Task<CleanResult> CleanAsync(string input, CleanContext context, CancellationToken ct = default)
    {
        if (!context.UseAiClean)
        {
            _logger.LogDebug("AI cleaning skipped per request for {Url}", context.Url);
            return new CleanResult { Output = input, AiCleaned = false };
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
        await using var handle = await _redis.GetLock($"AIClean:{hash}").AcquireAsync(TimeSpan.FromSeconds(30), ct);

        try
        {
            var (output, usage) = await _aiCleaner.CleanAsync(input, ct);
            if (string.IsNullOrWhiteSpace(output))
                _logger.LogWarning("AI returned empty result for {Url}", context.Url);
            return new CleanResult { Output = output, AiCleaned = true, TokenUsage = usage };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "AI cleaning failed for {Url}, returning rule-based result", context.Url);
            return new CleanResult { Output = input, AiCleaned = false };
        }
    }
}
