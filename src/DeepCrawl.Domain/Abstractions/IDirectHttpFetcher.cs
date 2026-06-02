namespace DeepCrawl.Domain.Abstractions;

public interface IDirectHttpFetcher
{
    Task<string> FetchDirectAsync(string url, CancellationToken ct = default);
    Task<string> FetchWithProxyAsync(string url, CancellationToken ct = default);
}
