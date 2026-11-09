using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Settles up with Stax in the background: marks bank payments paid once the bank has settled them and carries out
/// refunds that had to wait (an ACH payment cannot be refunded until a few business days after it was made).
/// </summary>
public class PaymentReconciliationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PaymentReconciliationService> _logger;

    public PaymentReconciliationService(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<PaymentReconciliationService> logger)
    {
        _scopes = scopes;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var seconds = int.TryParse(_configuration["Stax:ReconcileIntervalSeconds"], out var s) && s > 0 ? s : 60;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(seconds), stoppingToken);
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Payment reconciliation failed; will retry.");
            }
        }
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        var orders = scope.ServiceProvider.GetRequiredService<OrderPaymentService>();

        var ids = await db.OrderPayments
            .Where(p => p.Status == PaymentStatuses.Pending || p.RefundStatus == RefundStatuses.Pending)
            .Select(p => p.Id).ToListAsync(ct);
        foreach (var id in ids)
        {
            var payment = await db.OrderPayments.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (payment == null) continue;
            await orders.ReconcileAsync(payment, ct);
        }
    }
}
