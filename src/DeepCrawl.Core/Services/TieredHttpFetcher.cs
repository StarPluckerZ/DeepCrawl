using DeepCrawl.Domain.Abstractions;
using DeepCrawl.Domain.Enums;
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
    private const int DEFAULT_TIMEOUT_SECONDS = 30;
    private readonly SemaphoreSlim _httpSem = new(crawlConfig.HttpConcurrent);
    private readonly SemaphoreSlim _cloakSem = new(crawlConfig.CloakConcurrent);

    public async Task<(bool Success, FetchTier Tier, string? Html, string? Error)> FetchAsync(
        string url, string? waitUntil, CancellationToken ct)
    {
        var proxyConfigured = crawlConfig.ProxyConfigured;
        var proxyUrl = crawlConfig.ProxyUrl;
        string? html = null;
        var success = false;
        var jsSkeleton = false;
        var tier1NetworkOk = false;
        var tier = FetchTier.HttpClient;
        string? lastError = null;

        // Tier 1: HttpClient direct
        await _httpSem.WaitAsync(ct);
        try
        {
            html = await directFetcher.FetchDirectAsync(url, ct);
            tier1NetworkOk = true;
            if (contentAnalyzer.GetTextLength(html) >= crawlConfig.MinTextLength)
                success = true;
            else
            {
                jsSkeleton = true;
                logger.LogWarning("Tier 1 (HttpClient) got JS skeleton for {Url}, falling to browser", url);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Tier 1 (HttpClient) timed out for {Url}, falling back", url);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Tier 1 (HttpClient) failed for {Url}: {Msg}, falling back", url, ex.Message);
        }
        finally
        {
            _httpSem.Release();
        }

        // Tier 2: HttpClient + proxy
        if (!success && proxyConfigured && !jsSkeleton)
        {
            tier = FetchTier.HttpClientProxy;
            await _httpSem.WaitAsync(ct);
            try
            {
                html = await directFetcher.FetchWithProxyAsync(url, ct);
                if (contentAnalyzer.GetTextLength(html) >= crawlConfig.MinTextLength)
                    success = true;
                else
                {
                    jsSkeleton = true;
                    logger.LogWarning("Tier 2 (HttpClient+proxy) got JS skeleton for {Url}, falling to browser", url);
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning("Tier 2 (HttpClient+proxy) timed out for {Url}, falling back", url);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Tier 2 (HttpClient+proxy) failed for {Url}: {Msg}, falling back", url, ex.Message);
            }
            finally
            {
                _httpSem.Release();
            }
        }

        // Tier 3: Cloak browser
        var skipTier3 = jsSkeleton && !tier1NetworkOk && proxyConfigured;
        if (!success && !skipTier3)
        {
            tier = FetchTier.CloakBrowser;
            var t3Timeout = jsSkeleton ? 30 : DEFAULT_TIMEOUT_SECONDS;
            var t3Wait = (jsSkeleton && !proxyConfigured) ? "networkidle" : waitUntil;
            using var t3cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            t3cts.CancelAfter(TimeSpan.FromSeconds(t3Timeout));
            await _cloakSem.WaitAsync(ct);
            try
            {
                html = await cloakClient.FetchHtmlAsync(url, t3Wait, null, t3cts.Token);
                if (!string.IsNullOrWhiteSpace(html))
                    success = true;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning("Tier 3 (Cloak) timed out after {Timeout}s for {Url}", t3Timeout, url);
            }
            catch (CloakBrowserException ex)
            {
                lastError = ex.Message;
                logger.LogWarning("Tier 3 (Cloak) failed for {Url}: {Msg}, falling back", url, ex.Message);
            }
            finally
            {
                _cloakSem.Release();
            }
        }

        // Tier 4: Cloak browser + proxy
        if (!success && proxyConfigured)
        {
            tier = FetchTier.CloakBrowserProxy;
            await _cloakSem.WaitAsync(ct);
            try
            {
                html = await cloakClient.FetchHtmlAsync(url, null, proxyUrl, ct);
                if (!string.IsNullOrWhiteSpace(html))
                    success = true;
            }
            catch (CloakBrowserException ex)
            {
                lastError = ex.Message;
                logger.LogWarning("Tier 4 (Cloak+proxy) failed for {Url}: {Msg}", url, ex.Message);
            }
            finally
            {
                _cloakSem.Release();
            }
        }

        if (success)
            logger.LogInformation("Tier {Tier} ({TierName}) succeeded for {Url}", (int)tier, tier, url);

        return success ? (true, tier, html, null) : (false, tier, null, lastError);
    }
}
