using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Settles signups whose outcome was unknown (knowledge/rules.md R11) without waiting for the shopper
/// to try again: once 120 s have passed since the create was first sent, it reads the reference back
/// and, if nothing holds it, resends with the same token (or a new one where R11 step 4 allows it).
/// </summary>
public class SubscriptionSignupReconciler : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);
    private const int BatchSize = 20;

    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _time;
    private readonly ILogger<SubscriptionSignupReconciler> _logger;

    public SubscriptionSignupReconciler(IServiceScopeFactory scopes, TimeProvider time,
        ILogger<SubscriptionSignupReconciler> logger)
    {
        _scopes = scopes;
        _time = time;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, _time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ReconcileOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Signup reconciliation pass failed; retrying in {Interval}", Interval);
            }
        }
    }

    public async Task ReconcileOnceAsync(CancellationToken cancellationToken)
    {
        string[] due;
        using (var scope = _scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CatalogContext>();
            var cutoff = _time.GetUtcNow() - MaxioSubscriptionService.UnknownOutcomeWindow;
            var candidates = await db.SubscriptionSignups.AsNoTracking()
                .Where(s => s.Status == SubscriptionSignupStatus.Unknown || s.Status == SubscriptionSignupStatus.Pending)
                .OrderBy(s => s.UpdatedAt)
                .Take(BatchSize * 5)
                .ToListAsync(cancellationToken);
            var now = _time.GetUtcNow();
            due = candidates
                .Where(s => !s.IsLeased(now) && s.UpdatedAt <= cutoff)
                .Take(BatchSize)
                .Select(s => s.Id)
                .ToArray();
        }

        foreach (var id in due)
        {
            using var scope = _scopes.CreateScope();
            if (scope.ServiceProvider.GetRequiredService<ISubscriptionService>() is not MaxioSubscriptionService service)
                return;
            try
            {
                await service.ResumeAsync(id, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Could not settle signup {SignupId} yet", id);
            }
        }
    }
}
