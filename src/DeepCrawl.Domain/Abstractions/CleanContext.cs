using System.Security.Cryptography;
using System.Text;

namespace DeepCrawl.Domain.Abstractions;

public class CleanContext
{
    public string Url { get; init; } = null!;
    public bool UseAiClean { get; init; } = true;
    public IList<string> Formats { get; init; } = new List<string>();
    public int? StatusCode { get; set; }
    public string? ContentType { get; set; }
    public CrawlMetadata? Metadata { get; set; }

    public string ComputeContextHash()
    {
        var key = $"UseAiClean={UseAiClean}";
        var bytes = Encoding.UTF8.GetBytes(key);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}
