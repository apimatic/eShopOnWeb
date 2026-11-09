using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.Payments;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Stax;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Bank payments settle days after the shopper pays, and a bank refund is only allowed days after the payment.
/// This keeps orders current without anyone checking by hand: it marks settled bank payments paid and
/// retries refunds that Stax could not accept yet.
/// </summary>
public class PaymentReconciliationService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    private readonly IServiceScopeFactory _scopes;
    private readonly IOptionsMonitor<StaxSettings> _settings;
    private readonly ILogger<PaymentReconciliationService> _log;

    public PaymentReconciliationService(IServiceScopeFactory scopes, IOptionsMonitor<StaxSettings> settings, ILogger<PaymentReconciliationService> log)
    {
        _scopes = scopes;
        _settings = settings;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); } catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ReconcileAsync(stoppingToken); }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { _log.LogError(ex, "Payment reconciliation failed."); }

            try { await Task.Delay(Interval, stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }

    public async Task ReconcileAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_settings.CurrentValue.ApiKey)) return;

        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        var orders = scope.ServiceProvider.GetRequiredService<OrderPaymentService>();

        var ids = await db.ShopperOrders
            .Where(o => o.PaymentState == PaymentState.Pending || o.RefundState == RefundState.Pending)
            .Select(o => o.Id).ToListAsync(ct);
        foreach (var id in ids)
        {
            try
            {
                var order = await db.ShopperOrders.FirstAsync(o => o.Id == id, ct);
                await orders.RefreshAsync(order, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Could not reconcile order {OrderId}.", id);
                db.ChangeTracker.Clear();
            }
        }
    }
}
