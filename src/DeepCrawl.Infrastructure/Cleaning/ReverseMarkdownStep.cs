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
        // Input is already markdown (Zhipu reader tier) — skip the HTML → markdown conversion
        if (context.ContentType == "text/markdown")
            return Task.FromResult(new CleanResult { Output = input, AiCleaned = false });

        return Task.FromResult(new CleanResult { Output = _converter.Convert(input), AiCleaned = false });
    }
}
