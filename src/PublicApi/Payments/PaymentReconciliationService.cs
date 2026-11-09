using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Checks with Stax, on a timer, on bank payments awaiting settlement and on refunds in progress, so that orders
/// show as paid (and refunds complete) without anyone checking by hand.
/// </summary>
public class PaymentReconciliationService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<PaymentReconciliationService> _logger;

    public PaymentReconciliationService(IServiceScopeFactory scopes, ILogger<PaymentReconciliationService> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // let the application finish starting (database seeding) first
        if (!await Wait(TimeSpan.FromSeconds(10), stoppingToken))
        {
            return;
        }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var orders = scope.ServiceProvider.GetRequiredService<OrderPaymentService>();
                await orders.SyncAllPendingAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Payment reconciliation failed; will retry.");
            }
            if (!await Wait(Interval, stoppingToken))
            {
                return;
            }
        }
    }

    private static async Task<bool> Wait(TimeSpan delay, CancellationToken token)
    {
        try
        {
            await Task.Delay(delay, token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
