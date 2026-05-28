using System.Text.RegularExpressions;
using DeepCrawl.Domain.Abstractions;
using DeepCrawl.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace DeepCrawl.Infrastructure.Cleaning;

public partial class StripDataUriStep : ICleanStep
{
    private readonly ILogger<StripDataUriStep> _logger;

    public StripDataUriStep(ILogger<StripDataUriStep> logger)
    {
        _logger = logger;
    }

    public CleanStage Stage => CleanStage.Html;
    public int Order => 20;

    [GeneratedRegex(@"src\s*=\s*['""]data:image/[^'""]+['""]", RegexOptions.IgnoreCase)]
    private static partial Regex SrcDataUriPattern();

    [GeneratedRegex(@"src\s*=\s*['""]data:video/[^'""]+['""]", RegexOptions.IgnoreCase)]
    private static partial Regex SrcDataVideoPattern();

    [GeneratedRegex(@"src\s*=\s*['""]data:audio/[^'""]+['""]", RegexOptions.IgnoreCase)]
    private static partial Regex SrcDataAudioPattern();

    [GeneratedRegex(@"href\s*=\s*['""]data:[^'""]+['""]", RegexOptions.IgnoreCase)]
    private static partial Regex HrefDataUriPattern();

    [GeneratedRegex(@"url\(data:[^)]+\)", RegexOptions.IgnoreCase)]
    private static partial Regex CssDataUriPattern();

    public Task<CleanResult> CleanAsync(string input, CleanContext context, CancellationToken ct = default)
    {
        var before = input.Length;
        input = SrcDataUriPattern().Replace(input, "");
        input = SrcDataVideoPattern().Replace(input, "");
        input = SrcDataAudioPattern().Replace(input, "");
        input = HrefDataUriPattern().Replace(input, "");
        input = CssDataUriPattern().Replace(input, "");
        var after = input.Length;

        _logger.LogDebug("StripDataUri: {Before} -> {After} chars (removed {Diff})", before, after, before - after);
        return Task.FromResult(new CleanResult { Output = input, AiCleaned = false });
    }
}
