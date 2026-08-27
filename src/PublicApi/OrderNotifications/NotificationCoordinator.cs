using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Twilio;

namespace Microsoft.eShopWeb.PublicApi.OrderNotifications;

public sealed class NotificationCoordinator
{
    private static readonly SemaphoreSlim ResendLock = new(1, 1);
    private readonly CatalogContext _db;
    private readonly ITwilioMessagingClient _messaging;

    public NotificationCoordinator(CatalogContext db, ITwilioMessagingClient messaging)
    {
        _db = db;
        _messaging = messaging;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken)
        => await SendToActiveContactsAsync(
            order,
            NotificationKind.OrderPlaced,
            $"Your eShop order #{order.Id} has been placed.",
            null,
            cancellationToken);

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken)
    {
        await SendToActiveContactsAsync(
            order,
            NotificationKind.OrderDispatched,
            $"Your eShop order #{order.Id} is on its way.",
            null,
            cancellationToken);

        await SendToActiveContactsAsync(
            order,
            NotificationKind.DeliveryFollowUp,
            $"How did delivery of your eShop order #{order.Id} go?",
            DateTimeOffset.UtcNow.AddDays(3),
            cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken)
        => await SendToActiveContactsAsync(
            order,
            NotificationKind.OrderCancelled,
            $"Your eShop order #{order.Id} has been cancelled.",
            null,
            cancellationToken);

    public async Task CancelPendingFollowUpsForOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var pending = await _db.OrderNotifications
            .Where(x => x.OrderId == orderId &&
                        x.Kind == NotificationKind.DeliveryFollowUp &&
                        x.ProviderMessageSid != null &&
                        (x.ProviderStatus == "scheduled" || x.ProviderStatus == "queued" || x.ProviderStatus == "accepted"))
            .ToListAsync(cancellationToken);

        foreach (var notification in pending)
        {
            await TryCancelAsync(notification, cancellationToken);
        }

        await _db.SaveChangesAsync(CancellationToken.None);
    }

    public async Task CancelPendingFollowUpsForContactAsync(int contactNumberId, CancellationToken cancellationToken)
    {
        var pending = await _db.OrderNotifications
            .Where(x => x.ContactNumberId == contactNumberId &&
                        x.Kind == NotificationKind.DeliveryFollowUp &&
                        x.ProviderMessageSid != null &&
                        (x.ProviderStatus == "scheduled" || x.ProviderStatus == "queued" || x.ProviderStatus == "accepted"))
            .ToListAsync(cancellationToken);

        foreach (var notification in pending)
        {
            await TryCancelAsync(notification, cancellationToken);
        }

        await _db.SaveChangesAsync(CancellationToken.None);
    }

    public async Task RefreshAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications.Where(x => x.ProviderMessageSid != null))
        {
            try
            {
                var provider = await _messaging.FetchAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.RecordProviderStatus(provider.Status, provider.ErrorCode, DateTimeOffset.UtcNow);
            }
            catch (Exception)
            {
                // A status refresh is best effort. The last known provider state remains reportable.
            }
        }

        await _db.SaveChangesAsync(CancellationToken.None);
    }

    public async Task<OrderNotification> ResendAsync(
        int notificationId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await ResendLock.WaitAsync(cancellationToken);
        try
        {
            var existing = await _db.OrderNotifications.SingleOrDefaultAsync(
                x => x.ResendOfNotificationId == notificationId && x.IdempotencyKey == idempotencyKey,
                cancellationToken);
            if (existing != null)
            {
                return existing;
            }

            var source = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken)
                ?? throw new NotificationNotFoundException();
            await RefreshAsync(new[] { source }, cancellationToken);

            if (source.ContentRedacted || string.IsNullOrWhiteSpace(source.Body))
            {
                throw new NotificationConflictException("A notification with disposed content cannot be resent.");
            }

            if (source.ProviderStatus is not ("failed" or "undelivered"))
            {
                throw new NotificationConflictException("Only a failed or undelivered notification can be resent.");
            }

            var order = await _db.Orders.SingleOrDefaultAsync(x => x.Id == source.OrderId, cancellationToken)
                ?? throw new NotificationNotFoundException();
            if (order.Status == OrderStatus.Cancelled)
            {
                throw new NotificationConflictException("Notifications for a cancelled order cannot be resent.");
            }

            var contact = await _db.ContactNumbers.SingleOrDefaultAsync(
                x => x.Id == source.ContactNumberId && x.DeletedAt == null,
                cancellationToken);
            if (contact == null)
            {
                throw new NotificationConflictException("The destination is no longer registered.");
            }

            var resend = new OrderNotification(
                source.OrderId,
                source.BuyerId,
                source.ContactNumberId,
                NotificationKind.Resend,
                source.Body,
                DateTimeOffset.UtcNow,
                resendOfNotificationId: source.Id,
                idempotencyKey: idempotencyKey);
            _db.OrderNotifications.Add(resend);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                _db.Entry(resend).State = EntityState.Detached;
                var concurrent = await _db.OrderNotifications.SingleOrDefaultAsync(
                    x => x.ResendOfNotificationId == notificationId && x.IdempotencyKey == idempotencyKey,
                    cancellationToken);
                if (concurrent != null)
                {
                    return concurrent;
                }

                throw;
            }

            await SendAsync(resend, contact.PhoneNumber, null, cancellationToken);
            return resend;
        }
        finally
        {
            ResendLock.Release();
        }
    }

    public async Task RedactAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (notification.ContentRedacted)
        {
            return;
        }

        if (notification.ProviderMessageSid != null)
        {
            var provider = await _messaging.RedactAsync(notification.ProviderMessageSid, cancellationToken);
            notification.RecordProviderStatus(provider.Status, provider.ErrorCode, DateTimeOffset.UtcNow);
        }

        notification.Redact(DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task SendToActiveContactsAsync(
        Order order,
        NotificationKind kind,
        string body,
        DateTimeOffset? scheduledFor,
        CancellationToken cancellationToken)
    {
        var contacts = await _db.ContactNumbers
            .Where(x => x.BuyerId == order.BuyerId && x.DeletedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var contact in contacts)
        {
            var notification = new OrderNotification(
                order.Id,
                order.BuyerId,
                contact.Id,
                kind,
                body,
                DateTimeOffset.UtcNow,
                scheduledFor);
            _db.OrderNotifications.Add(notification);
            await _db.SaveChangesAsync(cancellationToken);
            await SendAsync(notification, contact.PhoneNumber, scheduledFor, cancellationToken);
        }
    }

    private async Task SendAsync(
        OrderNotification notification,
        string destination,
        DateTimeOffset? scheduledFor,
        CancellationToken cancellationToken)
    {
        try
        {
            var provider = await _messaging.CreateAsync(destination, notification.Body!, scheduledFor, cancellationToken);
            notification.RecordProviderResult(provider.Sid, provider.Status, provider.ErrorCode, DateTimeOffset.UtcNow);
        }
        catch (TwilioApiException exception)
        {
            notification.RecordFailure(exception.ProviderCode, DateTimeOffset.UtcNow);
        }
        catch (Exception)
        {
            notification.RecordFailure(null, DateTimeOffset.UtcNow);
        }

        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task TryCancelAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var provider = await _messaging.CancelAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.RecordProviderStatus(provider.Status, provider.ErrorCode, DateTimeOffset.UtcNow);
                return;
            }
            catch (Exception)
            {
                // Updating a scheduled message is idempotent, so bounded retries are safe.
            }
        }
    }
}

public sealed class NotificationNotFoundException : Exception;

public sealed class NotificationConflictException : Exception
{
    public NotificationConflictException(string message) : base(message) { }
}
