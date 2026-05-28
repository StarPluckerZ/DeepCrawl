using DeepCrawl.Domain.Abstractions;
using DeepCrawl.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace DeepCrawl.Core.Services;

public class CleanPipeline
{
    private readonly IEnumerable<ICleanStep> _steps;
    private readonly ILogger<CleanPipeline> _logger;

    public CleanPipeline(IEnumerable<ICleanStep> steps, ILogger<CleanPipeline> logger)
    {
        _steps = steps.OrderBy(s => s.Stage).ThenBy(s => s.Order).ToList();
        _logger = logger;
    }

    public async Task<CleanResult> ExecuteAsync(string input, CleanContext context, CancellationToken ct = default)
    {
        var htmlSteps = _steps.Where(s => s.Stage == CleanStage.Html).ToList();
        var markdownSteps = _steps.Where(s => s.Stage == CleanStage.Markdown).ToList();

        var aiCleaned = false;
        string? cleanedHtml = null;

        foreach (var step in htmlSteps)
        {
            _logger.LogDebug("Running HTML step {Step} (Order={Order})", step.GetType().Name, step.Order);
            var result = await step.CleanAsync(input, context, ct);
            input = result.Output;
            aiCleaned = aiCleaned || result.AiCleaned;
        }

        cleanedHtml = input;

        foreach (var step in markdownSteps)
        {
            _logger.LogDebug("Running Markdown step {Step} (Order={Order})", step.GetType().Name, step.Order);
            var result = await step.CleanAsync(input, context, ct);
            input = result.Output;
            aiCleaned = aiCleaned || result.AiCleaned;
        }

        return new CleanResult
        {
            Output = input,
            CleanedHtml = cleanedHtml,
            AiCleaned = aiCleaned,
            Metadata = context.Metadata
        };
    }
}
