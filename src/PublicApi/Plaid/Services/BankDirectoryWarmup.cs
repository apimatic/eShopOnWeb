using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Plaid.Services;

/// <summary>
/// Loads the bank list when the app starts and refreshes it ahead of expiry, so the first shopper to open the bank
/// picker doesn't wait for the full paging of Plaid's institutions.
/// </summary>
public sealed class BankDirectoryWarmup : BackgroundService
{
    private static readonly TimeSpan Refresh = TimeSpan.FromHours(5);
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(2);

    private readonly IServiceProvider _services;
    private bool _warmedOnce;
    private readonly ILogger<BankDirectoryWarmup> _logger;

    public BankDirectoryWarmup(IServiceProvider services, ILogger<BankDirectoryWarmup> logger)
    {
        _services = services;
        _logger = logger;
    }

    private static async Task<System.Collections.Generic.IReadOnlyList<BankInfo>> RefreshAndGet(BankDirectoryService directory, CancellationToken ct)
    {
        await directory.RefreshAsync();
        return await directory.GetBanksAsync(null, ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = Refresh;
            try
            {
                var directory = _services.GetRequiredService<BankDirectoryService>();
                var banks = _warmedOnce ? await RefreshAndGet(directory, stoppingToken) : await directory.GetBanksAsync(null, stoppingToken);
                _warmedOnce = true;
                _logger.LogInformation("Bank list warmed up with {Count} banks.", banks.Count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Could not warm up the bank list; will try again.");
                delay = RetryAfterFailure;
            }

            try { await Task.Delay(delay, stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }
}
