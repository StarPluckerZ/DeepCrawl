using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DeepCrawl.Infrastructure.Filtering;

public class UBlacklistUpdateService : BackgroundService
{
    private readonly UBlacklistFilter _filter;
    private readonly UBlacklistOptions _options;
    private readonly ILogger<UBlacklistUpdateService> _logger;

    public UBlacklistUpdateService(UBlacklistFilter filter, UBlacklistOptions options, ILogger<UBlacklistUpdateService> logger)
    {
        _filter = filter;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("UBlacklist update service starting");

        try
        {
            await _filter.LoadRulesAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Initial UBlacklist load failed, will retry on next interval");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromHours(_options.UpdateIntervalHours), stoppingToken);
                await _filter.RefreshAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UBlacklist refresh failed");
            }
        }

        _logger.LogInformation("UBlacklist update service stopped");
    }
}
