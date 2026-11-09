using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.Payments;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Follows up on what Stax decides after the request is over: bank payments that settle days later,
/// and refunds Stax only allows once a transaction has batched or an ACH hold has passed.
/// </summary>
public class PaymentReconciliationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _config;
    private readonly ILogger<PaymentReconciliationService> _logger;

    public PaymentReconciliationService(IServiceScopeFactory scopes, IConfiguration config, ILogger<PaymentReconciliationService> logger)
    {
        _scopes = scopes;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var seconds = int.TryParse(_config["Stax:ReconcileIntervalSeconds"], out var s) && s > 0 ? s : 300;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReconcileAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Payment reconciliation failed.");
            }
            try { await Task.Delay(TimeSpan.FromSeconds(seconds), stoppingToken); }
            catch (OperationCanceledException) { }
        }
    }

    public async Task ReconcileAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        var service = scope.ServiceProvider.GetRequiredService<OrderPaymentService>();
        var open = await db.OrderPayments
            .Where(p => p.PaymentStatus == PaymentStatuses.Pending || p.RefundStatus == RefundStatuses.Pending)
            .ToListAsync(ct);
        foreach (var payment in open)
        {
            try
            {
                await service.RefreshPaymentAsync(payment, ct);
                await service.TryRefundAsync(payment, ct);
            }
            catch (ApiException ex)
            {
                _logger.LogWarning("Order {OrderId} not reconciled: {Reason}", payment.OrderId, ex.Message);
            }
        }
    }
}
