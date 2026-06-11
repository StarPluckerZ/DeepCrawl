namespace DeepCrawl.Domain.Abstractions;

public enum AfterActionReliability
{
    /// <summary>Fire-and-forget: does not block the search response. Errors are logged but not propagated.</summary>
    BestEffort = 0,

    /// <summary>Synchronous: must complete before the response is returned. Errors propagate to caller.</summary>
    Synchronous = 1,
}

public interface IAfterSearchAction
{
    AfterActionReliability Reliability { get; }

    Task ExecuteAsync(SearchContext context, CancellationToken ct = default);
}
