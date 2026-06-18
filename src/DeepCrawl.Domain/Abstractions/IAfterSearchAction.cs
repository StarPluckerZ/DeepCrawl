namespace DeepCrawl.Domain.Abstractions;

public enum ActionReliability
{
    /// <summary>Fire-and-forget: does not block the search response. Errors are logged but not propagated.</summary>
    BestEffort = 0,

    /// <summary>Synchronous: must complete before the response is returned. Errors propagate to caller.</summary>
    Synchronous = 1,
}

public interface IAfterSearchAction
{
    /// <summary>Execution order within the AfterSearch pipeline. Lower values run first. Default 0.</summary>
    int Order => 0;

    ActionReliability Reliability { get; }

    Task ExecuteAsync(SearchContext context, CancellationToken ct = default);
}
