using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.HostedServices;

public sealed class ScheduledMessageCancellationWorker : BackgroundService
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(1);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScheduledMessageCancellationWorker> _logger;

    public ScheduledMessageCancellationWorker(IServiceScopeFactory scopeFactory,
        ILogger<ScheduledMessageCancellationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(RetryInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await CancelPendingFollowUpsAsync(stoppingToken);
        }
    }

    private async Task CancelPendingFollowUpsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        var provider = scope.ServiceProvider.GetRequiredService<ITextMessagingProvider>();
        var cancelledOrderIds = db.Orders.Where(x => x.Status == OrderStatus.Cancelled).Select(x => x.Id);
        var pending = await db.OrderNotifications.Where(x =>
                cancelledOrderIds.Contains(x.OrderId) && x.Kind == NotificationKind.DeliveryFollowUp &&
                x.ProviderMessageSid != null &&
                (x.DeliveryStatus == NotificationDeliveryStatus.Scheduled ||
                 x.DeliveryStatus == NotificationDeliveryStatus.InProgress ||
                 x.DeliveryStatus == NotificationDeliveryStatus.ProviderRequestFailed ||
                 x.DeliveryStatus == NotificationDeliveryStatus.Unknown))
            .ToListAsync(cancellationToken);

        foreach (var notification in pending)
        {
            try
            {
                notification.ApplyProviderState(await provider.CancelAsync(notification.ProviderMessageSid!, cancellationToken));
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // Intentionally omit destination/provider details: contact numbers must never enter logs.
                _logger.LogWarning("Could not cancel scheduled notification {NotificationId}; it will be retried.",
                    notification.Id);
            }
        }
    }
}
