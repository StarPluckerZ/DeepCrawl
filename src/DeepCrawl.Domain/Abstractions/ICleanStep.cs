using DeepCrawl.Domain.Enums;

namespace DeepCrawl.Domain.Abstractions;

public interface ICleanStep
{
    CleanStage Stage { get; }
    int Order { get; }

    Task<CleanResult> CleanAsync(string input, CleanContext context, CancellationToken ct = default);
}
