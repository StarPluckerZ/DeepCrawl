using DeepCrawl.Domain.Abstractions;
using DeepCrawl.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DeepCrawl.Core.Services;

public class TieredHttpFetcher(
    IDirectHttpFetcher directFetcher,
    ICloakBrowserClient cloakClient,
    IContentAnalyzer contentAnalyzer,
    CrawlConfig crawlConfig,
    ILogger<TieredHttpFetcher> logger)
{
    public async Task<(bool Success, string? Html, string? Error)> FetchAsync(
        string url, string? waitUntil, CancellationToken ct)
    {
        var proxyConfigured = crawlConfig.ProxyConfigured;
        var proxyUrl = crawlConfig.ProxyUrl;
        string? html = null;
        var success = false;
        string? lastError = null;

        // Tier 1: HttpClient direct
        try
        {
            html = await directFetcher.FetchDirectAsync(url, ct);
            if (contentAnalyzer.GetTextLength(html) >= crawlConfig.MinTextLength)
                success = true;
            else
                logger.LogDebug("Tier 1 (HttpClient) got JS skeleton for {Url}", url);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogDebug("Tier 1 (HttpClient) timed out for {Url}", url);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug("Tier 1 (HttpClient) failed for {Url}: {Msg}", url, ex.Message);
        }

        // Tier 2: HttpClient + proxy
        if (!success && proxyConfigured)
        {
            try
            {
                html = await directFetcher.FetchWithProxyAsync(url, ct);
                if (contentAnalyzer.GetTextLength(html) >= crawlConfig.MinTextLength)
                    success = true;
                else
                    logger.LogDebug("Tier 2 (HttpClient+proxy) got JS skeleton for {Url}", url);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                logger.LogDebug("Tier 2 (HttpClient+proxy) timed out for {Url}", url);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug("Tier 2 (HttpClient+proxy) failed for {Url}: {Msg}", url, ex.Message);
            }
        }

        // Tier 3: Cloak browser
        if (!success)
        {
            try
            {
                html = await cloakClient.FetchHtmlAsync(url, waitUntil, null, ct);
                if (!string.IsNullOrWhiteSpace(html))
                    success = true;
            }
            catch (CloakBrowserException ex)
            {
                lastError = ex.Message;
                logger.LogDebug("Tier 3 (Cloak) failed for {Url}: {Msg}", url, ex.Message);
            }
        }

        // Tier 4: Cloak browser + proxy
        if (!success && proxyConfigured)
        {
            try
            {
                html = await cloakClient.FetchHtmlAsync(url, waitUntil, proxyUrl, ct);
                if (!string.IsNullOrWhiteSpace(html))
                    success = true;
            }
            catch (CloakBrowserException ex)
            {
                lastError = ex.Message;
                logger.LogDebug("Tier 4 (Cloak+proxy) failed for {Url}: {Msg}", url, ex.Message);
            }
        }

        return success ? (true, html, null) : (false, null, lastError);
    }
}
