using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Periodically settles subscription enrollments whose outcome is unknown (a create whose connection failed after
/// Maxio may have acted) or whose claiming request died, by re-reading Maxio by our reference.
/// </summary>
public sealed class BillingReconciliationService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BillingReconciliationService> _logger;

    public BillingReconciliationService(IServiceScopeFactory scopeFactory, ILogger<BillingReconciliationService> logger)
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
                var billing = scope.ServiceProvider.GetRequiredService<MaxioSubscriptionBillingService>();
                var settled = await billing.SettleUnsettledEnrollmentsAsync(userId: null, stoppingToken);
                if (settled > 0)
                {
                    _logger.LogInformation("Billing reconciliation processed {Count} unsettled enrollment(s)", settled);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Billing reconciliation pass failed");
            }
        }
    }
}
