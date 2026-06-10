namespace DeepCrawl.Domain.Abstractions;

public interface IAfterSearchAction
{
    Task ExecuteAsync(SearchContext context, CancellationToken ct = default);
}
