namespace DeepCrawl.Domain.Abstractions;

public interface ICloakBrowserClient
{
    Task<string> FetchHtmlAsync(string url, string? waitUntil = null, string? proxy = null, CancellationToken ct = default);
}

public class CloakBrowserException : Exception
{
    public string ErrorCode { get; }

    public CloakBrowserException(string errorCode, string message) : base(message)
    {
        ErrorCode = errorCode;
    }
}
