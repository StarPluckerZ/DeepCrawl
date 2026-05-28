using DeepCrawl.Domain.Abstractions;
using DeepCrawl.Domain.Enums;

namespace DeepCrawl.Infrastructure.Cleaning;

public class AngleSharpHtmlCleanerStep : ICleanStep
{
    private readonly IHtmlCleaner _cleaner;

    public AngleSharpHtmlCleanerStep(IHtmlCleaner cleaner)
    {
        _cleaner = cleaner;
    }

    public CleanStage Stage => CleanStage.Html;
    public int Order => 10;

    public async Task<CleanResult> CleanAsync(string input, CleanContext context, CancellationToken ct = default)
    {
        return new CleanResult { Output = await _cleaner.CleanAsync(input), AiCleaned = false };
    }
}
