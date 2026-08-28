using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public sealed class OrderNotificationDispatcher
{
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);
    private readonly CatalogContext _db;
    private readonly IMessagingProvider _provider;
    private readonly ILogger<OrderNotificationDispatcher> _logger;

    public OrderNotificationDispatcher(CatalogContext db, IMessagingProvider provider,
        ILogger<OrderNotificationDispatcher> logger)
    {
        _db = db;
        _provider = provider;
        _logger = logger;
    }

    public async Task NotifyActiveContactsAsync(int orderId, string ownerId, NotificationKind kind,
        string body, DateTimeOffset? scheduledFor, CancellationToken cancellationToken)
    {
        var contacts = await _db.ContactNumbers
            .Where(x => x.OwnerId == ownerId && x.RemovedAt == null)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

        foreach (var contact in contacts)
        {
            var notification = new OrderNotification(orderId, contact.Id, kind, body,
                DateTimeOffset.UtcNow, scheduledFor);
            _db.OrderNotifications.Add(notification);
            await _db.SaveChangesAsync(cancellationToken);
            await TrySendAsync(notification, contact.CanonicalNumber, CancellationToken.None);
        }
    }

    public async Task<OrderNotification> ResendAsync(OrderNotification original, ContactNumber contact,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        var existing = await _db.OrderNotifications.SingleOrDefaultAsync(
            x => x.OriginalNotificationId == original.Id && x.IdempotencyKey == idempotencyKey,
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var resend = new OrderNotification(original.OrderId, original.ContactNumberId,
            NotificationKind.Resend, original.Body!, DateTimeOffset.UtcNow, null, original.Id, idempotencyKey);
        _db.OrderNotifications.Add(resend);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _db.Entry(resend).State = EntityState.Detached;
            existing = await _db.OrderNotifications.SingleOrDefaultAsync(
                x => x.OriginalNotificationId == original.Id && x.IdempotencyKey == idempotencyKey,
                cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            throw;
        }

        await TrySendAsync(resend, contact.CanonicalNumber, CancellationToken.None);
        return resend;
    }

    public async Task RefreshAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        var changed = false;
        foreach (var notification in notifications.Where(x => x.ProviderMessageSid is not null))
        {
            try
            {
                var providerMessage = await _provider.GetAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.RefreshProviderState(providerMessage, DateTimeOffset.UtcNow);
                changed = true;
            }
            catch (Exception ex) when (ex is MessagingProviderException or HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning("Could not refresh provider state for notification {NotificationId}.", notification.Id);
            }
        }

        if (changed)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task CancelScheduledForOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var scheduled = await _db.OrderNotifications
            .Where(x => x.OrderId == orderId && x.Kind == NotificationKind.DeliveryFollowUp &&
                        x.ProviderMessageSid != null && x.ProviderStatus != "canceled" &&
                        x.ProviderStatus != "delivered" && x.ProviderStatus != "sent" &&
                        x.ProviderStatus != "failed" && x.ProviderStatus != "undelivered")
            .ToListAsync(cancellationToken);

        foreach (var notification in scheduled)
        {
            await TryCancelAsync(notification, cancellationToken);
        }
    }

    public async Task CancelScheduledForContactAsync(int contactNumberId, CancellationToken cancellationToken)
    {
        var scheduled = await _db.OrderNotifications
            .Where(x => x.ContactNumberId == contactNumberId && x.Kind == NotificationKind.DeliveryFollowUp &&
                        x.ProviderMessageSid != null && x.ProviderStatus != "canceled" &&
                        x.ProviderStatus != "delivered" && x.ProviderStatus != "sent" &&
                        x.ProviderStatus != "failed" && x.ProviderStatus != "undelivered")
            .ToListAsync(cancellationToken);

        foreach (var notification in scheduled)
        {
            await TryCancelAsync(notification, cancellationToken);
        }
    }

    private async Task TrySendAsync(OrderNotification notification, string to, CancellationToken cancellationToken)
    {
        try
        {
            var message = await _provider.SendAsync(to, notification.Body!, notification.ScheduledFor, cancellationToken);
            notification.RecordProviderMessage(message, DateTimeOffset.UtcNow);
        }
        catch (MessagingProviderException ex)
        {
            notification.RecordProviderFailure(ex.ProviderErrorCode, ex.Message, DateTimeOffset.UtcNow);
            _logger.LogWarning("Provider rejected notification {NotificationId} with code {ProviderErrorCode}.",
                notification.Id, ex.ProviderErrorCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            notification.RecordProviderFailure(null, "Twilio could not be reached.", DateTimeOffset.UtcNow);
            _logger.LogWarning("Provider was unavailable for notification {NotificationId}.", notification.Id);
        }

        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task TryCancelAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            var current = await _provider.GetAsync(notification.ProviderMessageSid!, cancellationToken);
            notification.RefreshProviderState(current, DateTimeOffset.UtcNow);
            if (current.Status == "scheduled")
            {
                var canceled = await _provider.CancelAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.RefreshProviderState(canceled, DateTimeOffset.UtcNow);
            }
        }
        catch (Exception ex) when (ex is MessagingProviderException or HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning("Could not cancel scheduled notification {NotificationId}.", notification.Id);
        }

        await _db.SaveChangesAsync(CancellationToken.None);
    }
}
