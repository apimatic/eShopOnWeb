using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Keeps orders honest without anyone checking by hand: bank payments become "paid" once the bank settles
/// them, and queued refunds are retried until Stax accepts them.
/// </summary>
public class StaxSyncService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IServiceProvider _services;
    private readonly ILogger<StaxSyncService> _logger;

    public StaxSyncService(IServiceProvider services, ILogger<StaxSyncService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnce(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Stax sync pass failed");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async Task RunOnce(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsContext>();
        var payments = scope.ServiceProvider.GetRequiredService<PaymentsService>();

        var pendingPayments = await db.Orders
            .Where(o => o.PaymentStatus == PaymentState.PENDING).Select(o => o.Id).ToListAsync(ct);
        foreach (var id in pendingPayments)
        {
            try
            {
                var order = await db.Orders.Include(o => o.Lines).FirstAsync(o => o.Id == id, ct);
                await payments.SyncPayment(order, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Could not sync payment of order {OrderId}", id);
            }
        }

        var pendingRefunds = await db.Orders
            .Where(o => o.RefundStatus == RefundState.PENDING).Select(o => o.Id).ToListAsync(ct);
        foreach (var id in pendingRefunds)
        {
            try
            {
                var order = await db.Orders.Include(o => o.Lines).FirstAsync(o => o.Id == id, ct);
                await payments.RetryRefund(order, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Could not retry refund of order {OrderId}", id);
            }
        }
    }
}
