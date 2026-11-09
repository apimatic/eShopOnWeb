using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Stax;

/// <summary>
/// A bank payment goes through days after it was made, and a bank refund can only be sent days after the payment.
/// This worker keeps checking Stax so that orders show as paid, and refunds get sent, without anyone doing it by hand.
/// </summary>
public class StaxSyncWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly StaxOptions _options;
    private readonly ILogger<StaxSyncWorker> _logger;

    public StaxSyncWorker(IServiceScopeFactory scopes, IOptions<StaxOptions> options, ILogger<StaxSyncWorker> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(5, _options.SyncIntervalSeconds));
        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                using var scope = _scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<OrderPaymentService>().SyncPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is RequestRefusedException or InvalidOperationException or Microsoft.EntityFrameworkCore.DbUpdateException)
            {
                _logger.LogWarning(ex, "Stax sync failed; will retry");
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
