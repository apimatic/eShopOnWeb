using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Services;

/// <summary>
/// Retries only cancellation of provider-scheduled messages. Follow-ups themselves
/// are always scheduled and sent by Twilio, never by this process.
/// </summary>
public sealed class ScheduledMessageCancellationWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
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
        while (!stoppingToken.IsCancellationRequested)
        {
            await CancelOrphanedScheduledMessagesAsync(stoppingToken);
            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task CancelOrphanedScheduledMessagesAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        var twilio = scope.ServiceProvider.GetRequiredService<ITwilioGateway>();
        var candidates = await db.OrderNotifications.Where(x => x.ScheduledFor != null &&
            x.ProviderMessageSid != null && x.ProviderStatus != "canceled" &&
            x.ProviderStatus != "delivered" && x.ProviderStatus != "failed" &&
            x.ProviderStatus != "undelivered").ToListAsync(cancellationToken);

        foreach (var notification in candidates)
        {
            var orderCancelled = await db.Orders.AnyAsync(x => x.Id == notification.OrderId &&
                x.Status == OrderStatus.Cancelled, cancellationToken);
            var contactRemoved = await db.ContactNumbers.AnyAsync(x => x.Id == notification.ContactNumberId &&
                x.RemovedAt != null, cancellationToken);
            if (!orderCancelled && !contactRemoved) continue;

            try
            {
                var message = await twilio.CancelMessageAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.ApplyProviderState(message.Sid, message.Status, message.ErrorCode,
                    message.ErrorMessage, message.DateSent, message.DateUpdated);
            }
            catch (Exception ex) when (ex is TwilioProviderException or HttpRequestException or InvalidOperationException)
            {
                notification.MarkProviderFailure();
                _logger.LogWarning("A scheduled-message cancellation will be retried for notification {NotificationId}.",
                    notification.Id);
            }
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}
