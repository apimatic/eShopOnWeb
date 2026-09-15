using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public sealed class OrderNotificationService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ResendLocks = new();
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);
    private readonly CatalogContext _db;
    private readonly ITwilioMessagingClient _messaging;
    private readonly ILogger<OrderNotificationService> _logger;

    public OrderNotificationService(CatalogContext db, ITwilioMessagingClient messaging,
        ILogger<OrderNotificationService> logger)
    {
        _db = db;
        _messaging = messaging;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default) =>
        SendToCurrentContactsAsync(order, NotificationKind.OrderPlaced,
            $"Your eShop order #{order.Id} has been placed.", null, cancellationToken);

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        await SendToCurrentContactsAsync(order, NotificationKind.OrderDispatched,
            $"Your eShop order #{order.Id} has been dispatched and is on its way.", null, cancellationToken);
        await SendToCurrentContactsAsync(order, NotificationKind.DeliveryFollowUp,
            $"How did delivery of your eShop order #{order.Id} go?", DateTimeOffset.UtcNow.Add(FollowUpDelay),
            cancellationToken);
    }

    public Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default) =>
        SendToCurrentContactsAsync(order, NotificationKind.OrderCancelled,
            $"Your eShop order #{order.Id} has been cancelled.", null, cancellationToken);

    public async Task CancelOutstandingFollowUpsAsync(Order order, CancellationToken cancellationToken = default)
    {
        var notifications = await _db.OrderNotifications
            .Where(x => x.OrderId == order.Id && x.Kind == NotificationKind.DeliveryFollowUp &&
                        x.ProviderMessageSid != null)
            .ToListAsync(cancellationToken);

        foreach (var notification in notifications)
        {
            await TryCancelScheduledAsync(notification, strict: false, cancellationToken);
        }
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task CancelOutstandingForContactAsync(int contactNumberId,
        CancellationToken cancellationToken = default)
    {
        var notifications = await _db.OrderNotifications
            .Where(x => x.ContactNumberId == contactNumberId && x.Kind == NotificationKind.DeliveryFollowUp &&
                        x.ProviderMessageSid != null)
            .ToListAsync(cancellationToken);

        foreach (var notification in notifications)
        {
            await TryCancelScheduledAsync(notification, strict: true, cancellationToken);
        }
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task RefreshAsync(IEnumerable<OrderNotification> notifications,
        CancellationToken cancellationToken = default)
    {
        var changed = false;
        foreach (var notification in notifications.Where(x => x.ProviderMessageSid != null))
        {
            try
            {
                var current = await _messaging.FetchAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.UpdateProviderState(current.Status, current.ErrorCode, current.ErrorMessage,
                    current.DateSent);
                changed = true;
            }
            catch (Exception ex) when (ex is TwilioProviderException or HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning("Could not refresh notification {NotificationId} from the messaging provider.",
                    notification.Id);
            }
        }

        if (changed) await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<OrderNotification> ResendAsync(OrderNotification source, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var lockKey = $"{source.Id}:{idempotencyKey}";
        var gate = ResendLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var existing = await _db.OrderNotifications.FirstOrDefaultAsync(
                x => x.OriginalNotificationId == source.Id && x.IdempotencyKey == idempotencyKey,
                cancellationToken);
            if (existing != null) return existing;

            if (source.ProviderMessageSid != null)
            {
                try
                {
                    var current = await _messaging.FetchAsync(source.ProviderMessageSid, cancellationToken);
                    source.UpdateProviderState(current.Status, current.ErrorCode, current.ErrorMessage,
                        current.DateSent);
                    await _db.SaveChangesAsync(cancellationToken);
                }
                catch (TwilioProviderException)
                {
                    // The recorded outcome remains authoritative when the provider cannot be reached.
                }
            }

            if (!IsUnsuccessful(source.ProviderStatus))
                throw new InvalidOperationException("Only a notification that failed to reach the shopper can be resent.");
            if (source.Body == null)
                throw new InvalidOperationException("Disposed notification content cannot be resent.");

            var order = await _db.Orders.FirstOrDefaultAsync(x => x.Id == source.OrderId, cancellationToken)
                ?? throw new InvalidOperationException("The notification's order no longer exists.");
            var rootKind = await GetRootKindAsync(source, cancellationToken);
            if (order.Status == OrderStatus.Cancelled && rootKind == NotificationKind.DeliveryFollowUp)
                throw new InvalidOperationException("A delivery follow-up for a cancelled order cannot be resent.");

            var contact = await _db.ContactNumbers.FirstOrDefaultAsync(
                x => x.Id == source.ContactNumberId && x.OwnerId == source.BuyerId &&
                     x.CanonicalNumber == source.Destination, cancellationToken);
            if (contact == null)
                throw new InvalidOperationException("The destination contact number has been removed.");

            var resend = new OrderNotification(source.OrderId, source.BuyerId, source.ContactNumberId,
                source.Destination, NotificationKind.Resend, source.Body, originalNotificationId: source.Id,
                idempotencyKey: idempotencyKey,
                scheduledFor: rootKind == NotificationKind.DeliveryFollowUp && source.ScheduledFor > DateTimeOffset.UtcNow
                    ? source.ScheduledFor
                    : null);
            _db.OrderNotifications.Add(resend);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                _db.Entry(resend).State = EntityState.Detached;
                var concurrent = await _db.OrderNotifications.FirstOrDefaultAsync(
                    x => x.OriginalNotificationId == source.Id && x.IdempotencyKey == idempotencyKey,
                    cancellationToken);
                if (concurrent != null) return concurrent;
                throw;
            }
            await TrySendAsync(resend, cancellationToken);
            return resend;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task DisposeContentAsync(OrderNotification notification,
        CancellationToken cancellationToken = default)
    {
        if (notification.IsContentDisposed) return;
        if (notification.ProviderMessageSid != null)
        {
            var providerMessage = await _messaging.RedactContentAsync(notification.ProviderMessageSid,
                cancellationToken);
            notification.UpdateProviderState(providerMessage.Status, providerMessage.ErrorCode,
                providerMessage.ErrorMessage, providerMessage.DateSent);
        }
        notification.DisposeContent();
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task SendToCurrentContactsAsync(Order order, NotificationKind kind, string body,
        DateTimeOffset? scheduledFor, CancellationToken cancellationToken)
    {
        var contacts = await _db.ContactNumbers.Where(x => x.OwnerId == order.BuyerId)
            .ToListAsync(cancellationToken);
        foreach (var contact in contacts)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, contact.Id,
                contact.CanonicalNumber, kind, body, scheduledFor);
            _db.OrderNotifications.Add(notification);
            await _db.SaveChangesAsync(cancellationToken);
            await TrySendAsync(notification, cancellationToken);
        }
    }

    private async Task TrySendAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            var sent = await _messaging.SendAsync(notification.Destination, notification.Body!,
                notification.ScheduledFor, cancellationToken);
            if (string.IsNullOrWhiteSpace(sent.Sid))
                throw new TwilioProviderException("Twilio did not return a message identifier.");
            notification.RecordProviderState(sent.Sid, sent.Status, sent.ErrorCode, sent.ErrorMessage,
                sent.DateSent);
        }
        catch (TwilioProviderException ex)
        {
            notification.RecordSendFailure(ex.ProviderCode, "The messaging provider rejected the request.");
            _logger.LogWarning("Messaging provider rejected notification {NotificationId}.", notification.Id);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            notification.RecordSendFailure(null, "The messaging provider could not be reached.");
            _logger.LogWarning("Messaging provider was unavailable for notification {NotificationId}.",
                notification.Id);
        }
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task TryCancelScheduledAsync(OrderNotification notification, bool strict,
        CancellationToken cancellationToken)
    {
        try
        {
            var current = await _messaging.FetchAsync(notification.ProviderMessageSid!, cancellationToken);
            notification.UpdateProviderState(current.Status, current.ErrorCode, current.ErrorMessage,
                current.DateSent);
            if (IsPending(current.Status))
            {
                var cancelled = await _messaging.CancelAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.UpdateProviderState(cancelled.Status, cancelled.ErrorCode, cancelled.ErrorMessage,
                    cancelled.DateSent);
            }
        }
        catch (Exception ex) when (!strict && (ex is TwilioProviderException or HttpRequestException or TaskCanceledException))
        {
            notification.UpdateProviderState("cancel_failed", null,
                "The provider could not cancel the scheduled message.", notification.ProviderDateSent);
            _logger.LogError("Could not cancel scheduled notification {NotificationId} for a cancelled order.",
                notification.Id);
        }
    }

    private static bool IsPending(string status) => status is "accepted" or "scheduled" or "queued" or "sending";
    private static bool IsUnsuccessful(string status) => status is "failed" or "undelivered";

    private async Task<NotificationKind> GetRootKindAsync(OrderNotification notification,
        CancellationToken cancellationToken)
    {
        var current = notification;
        while (current.OriginalNotificationId.HasValue)
        {
            current = await _db.OrderNotifications.FirstOrDefaultAsync(
                x => x.Id == current.OriginalNotificationId.Value, cancellationToken)
                ?? throw new InvalidOperationException("The original notification no longer exists.");
        }
        return current.Kind;
    }
}
