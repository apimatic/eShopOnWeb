using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Keeps orders up to date without anyone checking by hand: a bank payment turns paid when the bank settles it,
/// and a refund that Stax could not take yet (bank payments) is retried until the shopper has the money back.
/// </summary>
public class PaymentReconciliationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IOptions<StaxSettings> _settings;
    private readonly ILogger<PaymentReconciliationService> _logger;

    public PaymentReconciliationService(IServiceScopeFactory scopes, IOptions<StaxSettings> settings, ILogger<PaymentReconciliationService> logger)
    {
        _scopes = scopes;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, _settings.Value.ReconcileIntervalSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, stoppingToken);
                if (string.IsNullOrWhiteSpace(_settings.Value.ApiKey)) continue;
                using var scope = _scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<PaymentService>().ReconcileAllAsync(stoppingToken);
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
}
