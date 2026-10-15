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

public sealed class BillingReconciliationWorker(IServiceScopeFactory scopes, ILogger<BillingReconciliationWorker> logger) : BackgroundService
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
                var settings = scope.ServiceProvider.GetRequiredService<MaxioSettings>();
                // Materialize before DateTimeOffset comparisons (SQL Server translation and InMemory agree).
                var pending = await db.BillingClaims.AsNoTracking().Where(x => x.Site == settings.Site && x.ProviderId == null)
                    .OrderBy(x => x.LastCheckedAt).Take(100).ToListAsync(stoppingToken);
                foreach (var claim in pending.Where(x => x.CreatedAt < DateTimeOffset.UtcNow.AddMinutes(-1)))
                {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    deadline.CancelAfter(TimeSpan.FromSeconds(15));
                    try { await scope.ServiceProvider.GetRequiredService<SubscriptionService>().ReconcileAsync(claim, deadline.Token); }
                    catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                    { logger.LogWarning("Billing reconciliation deferred: {FailureType}", ex.GetType().Name); }
                    finally
                    {
                        var tracked = await db.BillingClaims.FindAsync(new object[] { claim.Id }, stoppingToken);
                        if (tracked != null)
                        {
                            tracked.LastCheckedAt = DateTimeOffset.UtcNow;
                            await db.SaveChangesAsync(stoppingToken);
                        }
                    }
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            { logger.LogError("Billing reconciliation could not access its store: {FailureType}", ex.GetType().Name); }
        }
    }
}
