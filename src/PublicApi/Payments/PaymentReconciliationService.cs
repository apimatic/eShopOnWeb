using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Nobody checks by hand: this follows bank payments until the bank settles them, and keeps
/// retrying refunds that Stax could not complete straight away.
/// </summary>
public class PaymentReconciliationService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<PaymentReconciliationService> _logger;

    public PaymentReconciliationService(IServiceScopeFactory scopes, ILogger<PaymentReconciliationService> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Interval, stoppingToken);
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Payment reconciliation pass failed");
            }
        }
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StaxContext>();
        var orders = scope.ServiceProvider.GetRequiredService<OrderPaymentService>();

        var ids = await db.ShopOrders
            .Where(o => o.Status == OrderStatuses.PendingSettlement || o.Status == OrderStatuses.Processing || o.RefundStatus == RefundStatuses.Pending)
            .Select(o => o.Id).ToListAsync(ct);

        foreach (var id in ids)
        {
            var order = await db.ShopOrders.Include(o => o.Items).FirstAsync(o => o.Id == id, ct);
            await orders.RecoverProcessingAsync(order, ct);
            await orders.SyncPaymentAsync(order, ct);
            await orders.TryRefundAsync(order, ct);
        }
    }
}
