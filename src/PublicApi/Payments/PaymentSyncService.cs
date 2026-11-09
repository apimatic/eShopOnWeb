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
/// Keeps orders in step with Stax without anyone checking by hand: marks bank payments paid
/// once the bank settles them, and carries out refunds that Stax could not do straight away.
/// </summary>
public class PaymentSyncService : BackgroundService
{
    private static readonly TimeSpan INTERVAL = TimeSpan.FromSeconds(60);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<PaymentSyncService> _log;

    public PaymentSyncService(IServiceScopeFactory scopes, ILogger<PaymentSyncService> log)
    {
        _scopes = scopes;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(INTERVAL);
        do
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { _log.LogError(ex, "Payment sync pass failed."); }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var stax = scope.ServiceProvider.GetRequiredService<StaxClient>();
        if (!stax.HasApiKey) return;
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var orders = scope.ServiceProvider.GetRequiredService<ShopOrderService>();

        var pendingIds = await db.Orders.Where(o => o.PaymentState == PaymentState.PENDING)
            .OrderBy(o => o.Id).Select(o => o.Id).Take(200).ToListAsync(ct);
        foreach (var id in pendingIds)
        {
            try
            {
                var order = await db.Orders.Include(o => o.Lines).FirstAsync(o => o.Id == id, ct);
                await orders.RefreshPaymentAsync(order, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { _log.LogWarning(ex, "Could not refresh order {Id}", id); }
        }

        var now = DateTimeOffset.UtcNow;
        var refundIds = await db.Orders
            .Where(o => o.RefundStatus == RefundState.PENDING && (o.NextRefundAttemptAt == null || o.NextRefundAttemptAt <= now))
            .OrderBy(o => o.Id).Select(o => o.Id).Take(200).ToListAsync(ct);
        foreach (var id in refundIds)
        {
            try
            {
                var order = await db.Orders.Include(o => o.Lines).FirstAsync(o => o.Id == id, ct);
                await orders.TryRefundAsync(order, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { _log.LogWarning(ex, "Could not refund order {Id}", id); }
        }
    }
}
