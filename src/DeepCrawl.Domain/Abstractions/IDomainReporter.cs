namespace DeepCrawl.Domain.Abstractions;

public interface IDomainReporter
{
    Task RecordFailureAsync(string url, CancellationToken ct = default);
    Task RecordSuccessAsync(string url, CancellationToken ct = default);
}
