using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Moves orders along without anyone checking by hand: a bank payment becomes paid once the bank
/// settles it, and a refund staff asked for is carried through until the shopper has the money back.
/// </summary>
public sealed class PaymentReconciler : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly PaymentSettings _settings;
    private readonly ILogger<PaymentReconciler> _logger;

    public PaymentReconciler(IServiceScopeFactory scopes, IOptions<PaymentSettings> settings, ILogger<PaymentReconciler> logger)
    {
        _scopes = scopes;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var period = TimeSpan.FromSeconds(Math.Max(5, _settings.ReconcileSeconds));
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<OrderService>().ReconcileAsync();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Payment reconciliation pass failed");
                }
                await Task.Delay(period, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
