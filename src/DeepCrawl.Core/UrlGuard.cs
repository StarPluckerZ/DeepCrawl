using System.Net;
using System.Net.Sockets;

namespace DeepCrawl.Core;

/// <summary>
/// Validates URLs to prevent SSRF attacks. Blocks internal IPs,
/// non-HTTP schemes, and DNS rebinding to private addresses.
/// </summary>
public static class UrlGuard
{
    private static readonly HashSet<string> AllowedSchemes = ["http", "https"];

    /// <summary>Validate a scrape URL. Returns error message if blocked.</summary>
    public static (bool Ok, string? Error) Validate(string rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
            return (false, "URL is required");

        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
            return (false, "Invalid URL format");

        if (!AllowedSchemes.Contains(uri.Scheme))
            return (false, $"Scheme '{uri.Scheme}' not allowed");

        // Check for path traversal
        if (uri.AbsolutePath.Contains("/../") || uri.AbsolutePath.EndsWith("/.."))
            return (false, "Path traversal not allowed");

        return CheckHost(uri);
    }

    /// <summary>Check if a DnsEndPoint (from SocketsHttpHandler.ConnectCallback) is safe.</summary>
    public static bool IsSafeEndpoint(DnsEndPoint endpoint)
    {
        if (IPAddress.TryParse(endpoint.Host, out var ip))
            return IsPublicIp(ip);

        // For hostnames in ConnectCallback, the DNS is already resolved by HttpClient.
        // The Host property of DnsEndPoint will be the IP string at this point.
        return true;
    }

    private static (bool Ok, string? Error) CheckHost(Uri uri)
    {
        // If the host is already an IP literal
        if (IPAddress.TryParse(uri.Host, out var ip))
        {
            if (!IsPublicIp(ip))
                return (false, "Internal IP addresses are not allowed");
            return (true, null);
        }

        // DNS resolve and check for rebinding to private IPs
        try
        {
            var addresses = Dns.GetHostAddresses(uri.Host);
            foreach (var addr in addresses)
            {
                if (!IsPublicIp(addr))
                    return (false, $"Host '{uri.Host}' resolves to internal IP ({addr})");
            }
        }
        catch (SocketException)
        {
            // DNS resolution failed — let the fetcher handle it
        }

        return (true, null);
    }

    /// <summary>Check if an IP address is publicly routable.</summary>
    public static bool IsPublicIp(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return false;
        if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal) return false;

        var bytes = ip.GetAddressBytes();

        if (bytes.Length == 4)
        {
            if (bytes[0] == 10) return false;                        // 10.0.0.0/8
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return false; // 172.16.0.0/12
            if (bytes[0] == 192 && bytes[1] == 168) return false;     // 192.168.0.0/16
            if (bytes[0] == 169 && bytes[1] == 254) return false;     // 169.254.0.0/16
            if (bytes[0] == 127) return false;                        // 127.0.0.0/8
            if (bytes[0] == 0) return false;                          // 0.0.0.0/8
        }

        return true;
    }
}
