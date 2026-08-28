using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.eShopWeb.PublicApi.BackgroundServices;

public sealed class ScheduledNotificationCancellationWorker : BackgroundService
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(30);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;

    public ScheduledNotificationCancellationWorker(IServiceScopeFactory scopeFactory, TimeProvider timeProvider)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await RetryPendingCancellationsAsync(stoppingToken);
            await Task.Delay(RetryInterval, _timeProvider, stoppingToken);
        }
    }

    private async Task RetryPendingCancellationsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        var notifications = await db.OrderNotifications
            .Where(x => x.CancellationRequestedAt != null &&
                        x.CancellationCompletedAt == null &&
                        x.ProviderMessageSid != null)
            .ToListAsync(cancellationToken);
        if (notifications.Count == 0)
        {
            return;
        }

        var provider = scope.ServiceProvider.GetRequiredService<ITextMessageProvider>();
        foreach (var notification in notifications)
        {
            try
            {
                var current = await provider.GetAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.RecordProviderState(current, _timeProvider.GetUtcNow());
                if (string.Equals(current.Status, "scheduled", StringComparison.OrdinalIgnoreCase))
                {
                    var cancelled = await provider.CancelAsync(notification.ProviderMessageSid!, cancellationToken);
                    notification.RecordProviderState(cancelled, _timeProvider.GetUtcNow());
                }
                else
                {
                    notification.CompleteCancellationAttempt(_timeProvider.GetUtcNow());
                }
            }
            catch (TextMessageProviderException)
            {
                // Keep the durable request pending for the next bounded retry.
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
