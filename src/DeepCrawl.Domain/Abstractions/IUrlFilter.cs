namespace DeepCrawl.Domain.Abstractions;

public interface IUrlFilter
{
    Task LoadRulesAsync(CancellationToken ct = default);
    Task<bool> IsBlockedAsync(string url, CancellationToken ct = default);
}
