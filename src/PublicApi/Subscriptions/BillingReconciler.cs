using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

// Recovery reads only. An unresolved claim is never expired into permission to create again.
public sealed class BillingReconciler(IServiceScopeFactory scopes, ILogger<BillingReconciler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<CatalogContext>();
                var gateway = scope.ServiceProvider.GetRequiredService<MaxioGateway>();
                var store = scope.ServiceProvider.GetRequiredService<ClaimStore>();
                var cutoff = DateTimeOffset.UtcNow.AddMinutes(-1);
                // Materialize before comparing DateTimeOffset, also supported by the demo provider.
                var pending = await db.BillingClaims.AsNoTracking().Where(x => x.ProviderId == null).ToListAsync(stoppingToken);
                foreach (var claim in pending.Where(x => x.CreatedAt < cutoff).OrderBy(x => x.CreatedAt).Take(100))
                {
                    try
                    {
                        int? id = claim.Id.StartsWith("eshop-customer-", StringComparison.Ordinal)
                            ? (await gateway.FindCustomerAsync(claim.Id, stoppingToken))?.Id
                            : (await gateway.FindSubscriptionAsync(claim.Id, stoppingToken))?.Id;
                        if (id.HasValue) await store.CompleteAsync(claim.Id, id.Value, stoppingToken);
                        else logger.LogWarning("Billing claim remains unresolved; created {CreatedAt}", claim.CreatedAt);
                    }
                    catch (BillingException) { logger.LogWarning("Billing reconciliation deferred until next sweep"); }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError("Billing recovery sweep failed; kind {Kind}", ex.GetType().Name); }
        }
    }
}
