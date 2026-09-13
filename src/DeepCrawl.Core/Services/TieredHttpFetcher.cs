using DeepCrawl.Domain.Abstractions;
using DeepCrawl.Domain.Enums;
using DeepCrawl.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DeepCrawl.Core.Services;

public class TieredHttpFetcher(
    IDirectHttpFetcher directFetcher,
    ICloakBrowserClient cloakClient,
    IZhipuReaderFetcher zhipuReader,
    IContentAnalyzer contentAnalyzer,
    CrawlConfig crawlConfig,
    ILogger<TieredHttpFetcher> logger)
{
    private const int DEFAULT_TIMEOUT_SECONDS = 30;
    private readonly SemaphoreSlim _httpSem = new(crawlConfig.HttpConcurrent);
    private readonly SemaphoreSlim _cloakSem = new(crawlConfig.CloakConcurrent);
    private readonly SemaphoreSlim _readerSem = new(crawlConfig.ReaderConcurrent);

    public async Task<(bool Success, FetchTier Tier, string? Html, string? Error)> FetchAsync(
        string url, string? waitUntil, CancellationToken ct)
    {
        var proxyConfigured = crawlConfig.ProxyConfigured;
        var proxyUrl = crawlConfig.ProxyUrl;
        string? content = null;
        var success = false;
        var jsSkeleton = false;
        var tier1NetworkOk = false;
        var tier = FetchTier.HttpClient;
        string? lastError = null;

        // Tier 0: Zhipu reader (highest priority, returns markdown; skipped when not configured)
        if (crawlConfig.ZhipuReaderConfigured)
        {
            tier = FetchTier.ZhipuReader;
            await _readerSem.WaitAsync(ct);
            try
            {
                content = await zhipuReader.ReadAsync(url, ct);
                if (IsReaderContentValid(content))
                    success = true;
                else
                    logger.LogWarning("Tier 0 (Zhipu reader) returned no content for {Url}, falling back", url);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning("Tier 0 (Zhipu reader) timed out for {Url}, falling back", url);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Tier 0 (Zhipu reader) failed for {Url}: {Msg}, falling back", url, ex.Message);
            }
            finally
            {
                _readerSem.Release();
            }
        }

        // Tier 1: HttpClient direct
        tier = FetchTier.HttpClient;
        await _httpSem.WaitAsync(ct);
        try
        {
            content = await directFetcher.FetchDirectAsync(url, ct);
            tier1NetworkOk = true;
            if (contentAnalyzer.GetTextLength(content) >= crawlConfig.MinTextLength)
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
                content = await directFetcher.FetchWithProxyAsync(url, ct);
                if (contentAnalyzer.GetTextLength(content) >= crawlConfig.MinTextLength)
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
                content = await cloakClient.FetchHtmlAsync(url, t3Wait, null, t3cts.Token);
                if (!string.IsNullOrWhiteSpace(content))
                    success = true;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning("Tier 3 (Cloak) timed out after {Timeout}s for {Url}", t3Timeout, url);
            }
            catch (Exception ex) when (ex is CloakBrowserException or HttpRequestException)
            {
                // HttpRequestException = cloak service unreachable (e.g. not deployed) —
                // must degrade to a fetch failure, not an unhandled 500
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
                content = await cloakClient.FetchHtmlAsync(url, null, proxyUrl, ct);
                if (!string.IsNullOrWhiteSpace(content))
                    success = true;
            }
            catch (Exception ex) when (ex is CloakBrowserException or HttpRequestException)
            {
                lastError = ex.Message;
                logger.LogWarning("Tier 4 (Cloak+proxy) failed for {Url}: {Msg}", url, ex.Message);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                lastError = $"Tier 4 (Cloak+proxy) timed out for {url}";
                logger.LogWarning("Tier 4 (Cloak+proxy) timed out for {Url}", url);
            }
            finally
            {
                _cloakSem.Release();
            }
        }

        if (success)
            logger.LogInformation("Tier {Tier} ({TierName}) succeeded for {Url}", (int)tier, tier, url);

        return success ? (true, tier, content, null) : (false, tier, null, lastError);
    }

    // Zhipu reader returns already-extracted main content — the HTML-based heuristic
    // (ContentAnalyzer) does not apply, so use a plain length check instead
    private bool IsReaderContentValid(string? content)
        => !string.IsNullOrWhiteSpace(content) && content.Trim().Length >= crawlConfig.MinTextLength;
}
