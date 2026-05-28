using DeepCrawl.Domain.Abstractions;
using DeepCrawl.Domain.Enums;

namespace DeepCrawl.Infrastructure.Cleaning;

public class ReverseMarkdownStep : ICleanStep
{
    private readonly IMarkdownConverter _converter;

    public ReverseMarkdownStep(IMarkdownConverter converter)
    {
        _converter = converter;
    }

    public CleanStage Stage => CleanStage.Markdown;
    public int Order => 10;

    public Task<CleanResult> CleanAsync(string input, CleanContext context, CancellationToken ct = default)
    {
        return Task.FromResult(new CleanResult { Output = _converter.Convert(input), AiCleaned = false });
    }
}
