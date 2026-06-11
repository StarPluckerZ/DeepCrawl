namespace DeepCrawl.Domain.Abstractions;

public interface IRobotsTxtService
{
    /// <summary>
    /// Fetches the robots.txt file for the given page URL's origin.
    /// Returns the file content, or null if robots.txt does not exist or cannot be fetched.
    /// </summary>
    /// <param name="pageUrl">The page URL whose origin's robots.txt should be fetched.</param>
    /// <param name="useProxy">Whether to use the proxy HttpClient (e.g., when the main page required proxy).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<string?> FetchAsync(string pageUrl, bool useProxy, CancellationToken ct = default);
}
