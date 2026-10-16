using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class BillingReconciliationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<BillingReconciliationWorker> _logger;
    public BillingReconciliationWorker(IServiceScopeFactory scopes, ILogger<BillingReconciliationWorker> logger)
    { _scopes = scopes; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            using var scope = _scopes.CreateScope();
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            budget.CancelAfter(TimeSpan.FromSeconds(30));
            try { await scope.ServiceProvider.GetRequiredService<SubscriptionBillingService>().ReconcilePendingAsync(budget.Token); }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested) { _logger.LogWarning("Billing reconciliation budget elapsed."); }
            catch (Exception) when (!stoppingToken.IsCancellationRequested) { _logger.LogError("Billing reconciliation could not complete; pending claims retained."); }
        }
    }
}
