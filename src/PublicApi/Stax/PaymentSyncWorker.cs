using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Stax;

/// <summary>
/// Settles bank payments and completes pending refunds on its own, so nobody has to check by hand:
/// a bank payment is only final days after the shopper pays, and a refund may have to wait until Stax allows it.
/// </summary>
public class PaymentSyncWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<PaymentSyncWorker> _logger;

    public PaymentSyncWorker(IServiceScopeFactory scopes, ILogger<PaymentSyncWorker> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the host finish starting (and the database seed) before the first pass.
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken).ContinueWith(_ => { });
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Payment sync pass failed; will retry.");
            }
            await Task.Delay(Interval, stoppingToken).ContinueWith(_ => { });
        }
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        var orders = scope.ServiceProvider.GetRequiredService<ShopOrderService>();

        var ids = await db.Orders
            .Where(o => o.PaymentState == PaymentState.Pending || o.RefundStatus == RefundStatus.Pending)
            .Select(o => o.Id)
            .ToListAsync(ct);

        foreach (var id in ids)
        {
            var order = await db.Orders.Include(o => o.OrderItems).FirstOrDefaultAsync(o => o.Id == id, ct);
            if (order is not null) await orders.SyncAsync(order, ct);
        }
    }
}
