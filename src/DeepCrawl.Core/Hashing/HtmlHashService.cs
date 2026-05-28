using System.Security.Cryptography;
using System.Text;

namespace DeepCrawl.Core.Hashing;

public static class HtmlHashService
{
    public static string ComputeSha256(string rawHtml)
    {
        var bytes = Encoding.UTF8.GetBytes(rawHtml);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}
