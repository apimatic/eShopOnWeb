using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Keeps orders in line with Stax without anyone checking by hand: bank payments become paid once the
/// bank settles them, and refunds that had to wait (banks only allow ACH refunds after a few business days) go through.
/// </summary>
public class PaymentReconciler : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopes;
    private readonly IOptions<StaxSettings> _settings;
    private readonly ILogger<PaymentReconciler> _logger;

    public PaymentReconciler(IServiceScopeFactory scopes, IOptions<StaxSettings> settings, ILogger<PaymentReconciler> logger)
    {
        _scopes = scopes;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the app finish starting (database seeding) first.
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); } catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!string.IsNullOrWhiteSpace(_settings.Value.ApiKey))
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<OrderPaymentService>().Reconcile(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception e)
                {
                    _logger.LogWarning(e, "Payment reconciliation failed; it will be retried.");
                }
            }

            try { await Task.Delay(Interval, stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }
}
