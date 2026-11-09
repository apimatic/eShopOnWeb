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
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Stax;

/// <summary>
/// Follows payments that are still in flight at Stax: bank payments waiting to settle and refunds Stax has not yet allowed
/// (bank refunds only become possible some business days after the payment). Nobody has to check by hand.
/// </summary>
public class PaymentReconciler : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<PaymentReconciler> _logger;
    private readonly StaxOptions _options;

    public PaymentReconciler(IServiceScopeFactory scopes, ILogger<PaymentReconciler> logger, IOptions<StaxOptions> options)
    {
        _scopes = scopes;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); } catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_options.ApiKey)) await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Payment reconciliation failed");
            }
            try { await Task.Delay(Interval, stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        var orders = scope.ServiceProvider.GetRequiredService<OrderPayments>();

        var settling = await db.OrderPayments.AsNoTracking()
            .Where(p => p.Status == OrderPayment.PROCESSING && p.RefundStatus != OrderPayment.REFUND_REFUNDED)
            .Select(p => p.OrderId).ToListAsync(ct);
        var refunding = await db.OrderPayments.AsNoTracking()
            .Where(p => p.RefundStatus == OrderPayment.REFUND_PENDING)
            .Select(p => p.OrderId).ToListAsync(ct);

        foreach (var id in settling)
        {
            try { await orders.RefreshAsync(id, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogWarning(ex, "Settlement check failed for order {OrderId}", id); }
        }
        foreach (var id in refunding)
        {
            try { await orders.RetryRefundAsync(id, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogWarning(ex, "Refund retry failed for order {OrderId}", id); }
        }
    }
}
