using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Stax;

/// <summary>
/// Nobody checks by hand: a bank payment turns paid once the bank settles it, and a refund Stax could not make yet
/// (a bank payment cannot be returned straight away) is made as soon as Stax allows it.
/// </summary>
public class StaxReconciliationService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    private readonly IServiceScopeFactory _scopes;
    private readonly StaxClients _clients;
    private readonly ILogger<StaxReconciliationService> _logger;

    public StaxReconciliationService(IServiceScopeFactory scopes, StaxClients clients, ILogger<StaxReconciliationService> logger)
    {
        _scopes = scopes;
        _clients = clients;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                if (_clients.IsConfigured) await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reconciling payments with Stax failed.");
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

    public async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<OrderPaymentService>();
        foreach (var orderId in await orders.OpenOrderIdsAsync(ct))
        {
            try
            {
                using var orderScope = _scopes.CreateScope();
                await orderScope.ServiceProvider.GetRequiredService<OrderPaymentService>().RefreshAsync(orderId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Could not reconcile order {OrderId} with Stax.", orderId);
            }
        }
    }
}
