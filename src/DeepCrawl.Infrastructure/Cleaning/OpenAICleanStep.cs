using DeepCrawl.Domain.Abstractions;
using DeepCrawl.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace DeepCrawl.Infrastructure.Cleaning;

public class OpenAICleanStep : ICleanStep
{
    private readonly IAIMarkdownCleaner _aiCleaner;
    private readonly ILogger<OpenAICleanStep> _logger;

    public OpenAICleanStep(IAIMarkdownCleaner aiCleaner, ILogger<OpenAICleanStep> logger)
    {
        _aiCleaner = aiCleaner;
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

        try
        {
            var output = await _aiCleaner.CleanAsync(input, ct);
            if (string.IsNullOrWhiteSpace(output))
            {
                _logger.LogWarning("AI returned empty result for {Url}, falling back to rule-based result", context.Url);
                return new CleanResult { Output = input, AiCleaned = false };
            }
            return new CleanResult { Output = output, AiCleaned = true };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI cleaning failed for {Url}, returning rule-based result", context.Url);
            return new CleanResult { Output = input, AiCleaned = false };
        }
    }
}
