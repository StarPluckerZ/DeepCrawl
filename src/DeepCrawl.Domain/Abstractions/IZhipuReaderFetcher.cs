namespace DeepCrawl.Domain.Abstractions;

/// <summary>
/// Zhipu web reading client — fetches a page's parsed main content as markdown,
/// or returns null on any failure (so the fetch tier can fall back).
/// </summary>
public interface IZhipuReaderFetcher
{
    Task<string?> ReadAsync(string url, CancellationToken ct = default);
}
