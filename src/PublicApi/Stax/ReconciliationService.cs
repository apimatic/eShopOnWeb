using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Stax;

/// <summary>
/// Settles bank payments and finishes queued refunds in the background, so nobody has to check by hand:
/// a bank payment only settles days after it is made, and a bank refund can only start days after the payment.
/// </summary>
public class ReconciliationService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ReconciliationService> _logger;

    public ReconciliationService(IServiceScopeFactory scopes, ILogger<ReconciliationService> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken).ContinueWith(_ => { });
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ShopOrderService>().ReconcileAllAsync(stoppingToken);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Stax reconciliation pass failed; will retry.");
            }
            try { await Task.Delay(Interval, stoppingToken); } catch (OperationCanceledException) { }
        }
    }
}
