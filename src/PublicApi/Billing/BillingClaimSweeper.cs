using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Periodically settles billing claims whose provider outcome is unknown (or whose owner went away),
/// by re-reading Maxio with the reference the write carried.
/// </summary>
public class BillingClaimSweeper : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BillingClaimSweeper> _logger;

    public BillingClaimSweeper(IServiceScopeFactory scopeFactory, ILogger<BillingClaimSweeper> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var billing = scope.ServiceProvider.GetRequiredService<ISubscriptionBillingService>();
                var settled = await billing.SettleUnresolvedClaimsAsync(stoppingToken);
                if (settled > 0)
                {
                    _logger.LogInformation("Billing claim sweep settled {Count} claim(s).", settled);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Billing claim sweep failed; it will run again in {Interval}.", Interval);
            }
        }
    }
}
