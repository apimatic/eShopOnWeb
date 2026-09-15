using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Retries provider-side cancellation only. Follow-up delivery remains fully provider-scheduled.
/// </summary>
public sealed class PendingNotificationCancellationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PendingNotificationCancellationWorker> _logger;

    public PendingNotificationCancellationWorker(IServiceScopeFactory scopeFactory,
        ILogger<PendingNotificationCancellationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            await ProcessAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ProcessAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CatalogContext>();
            var gateway = scope.ServiceProvider.GetRequiredService<ITwilioMessagingGateway>();
            var pending = await db.OrderNotifications.Where(x =>
                x.CancellationRequestedAt != null && x.CancellationCompletedAt == null &&
                x.ProviderMessageSid != null).ToListAsync(ct);

            foreach (var notification in pending)
            {
                try
                {
                    var provider = await gateway.CancelAsync(notification.ProviderMessageSid!, ct);
                    notification.CompleteCancellation(provider.Status, DateTimeOffset.UtcNow);
                    await db.SaveChangesAsync(ct);
                }
                catch (TwilioProviderException)
                {
                    _logger.LogWarning("A provider-scheduled notification cancellation remains pending.");
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Pending notification cancellations could not be processed.");
        }
    }
}
