using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public sealed class OrderNotificationManager
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ResendLocks = new();
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);
    private readonly CatalogContext _db;
    private readonly ITwilioMessagingService _twilio;
    private readonly TimeProvider _clock;
    private readonly ILogger<OrderNotificationManager> _logger;

    public OrderNotificationManager(
        CatalogContext db,
        ITwilioMessagingService twilio,
        TimeProvider clock,
        ILogger<OrderNotificationManager> logger)
    {
        _db = db;
        _twilio = twilio;
        _clock = clock;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken)
    {
        return NotifyAllAsync(order, NotificationType.OrderPlaced,
            $"Your eShop order #{order.Id} has been placed.", null, cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken)
    {
        await NotifyAllAsync(order, NotificationType.OrderDispatched,
            $"Your eShop order #{order.Id} is on its way.", null, cancellationToken);

        var scheduledFor = _clock.GetUtcNow().Add(FollowUpDelay);
        await NotifyAllAsync(order, NotificationType.DeliveryFollowUp,
            $"How did delivery of eShop order #{order.Id} go?", scheduledFor, cancellationToken);
    }

    public Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken)
    {
        return NotifyAllAsync(order, NotificationType.OrderCancelled,
            $"Your eShop order #{order.Id} has been cancelled.", null, cancellationToken);
    }

    public async Task RequestCancellationForOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var notifications = await _db.OrderNotifications
            .Where(x => x.OrderId == orderId && x.Type == NotificationType.DeliveryFollowUp && x.ProviderStatus == "scheduled")
            .ToListAsync(cancellationToken);
        await RequestCancellationAsync(notifications, cancellationToken);
    }

    public async Task RequestCancellationForContactAsync(int contactNumberId, CancellationToken cancellationToken)
    {
        var notifications = await _db.OrderNotifications
            .Where(x => x.ContactNumberId == contactNumberId && x.ProviderStatus == "scheduled")
            .ToListAsync(cancellationToken);
        await RequestCancellationAsync(notifications, cancellationToken);
    }

    public async Task ProcessPendingCancellationsAsync(CancellationToken cancellationToken)
    {
        var notifications = await _db.OrderNotifications
            .Where(x => x.CancellationPending)
            .OrderBy(x => x.Id)
            .Take(100)
            .ToListAsync(cancellationToken);

        foreach (var notification in notifications)
        {
            await TryCancelAsync(notification, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<OrderNotification>> RefreshForOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var notifications = await _db.OrderNotifications
            .Where(x => x.OrderId == orderId)
            .OrderBy(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        foreach (var notification in notifications.Where(x => x.ProviderMessageSid != null))
        {
            await TryRefreshAsync(notification, cancellationToken);
        }

        await TrySaveChangesAsync(CancellationToken.None);
        return notifications;
    }

    public async Task<int> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 128)
        {
            throw new ArgumentException("An idempotency key of 1-128 characters is required.", nameof(idempotencyKey));
        }

        var normalizedKey = idempotencyKey.Trim();
        var lockKey = $"{notificationId}:{normalizedKey}";
        var gate = ResendLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var existing = await _db.OrderNotifications.SingleOrDefaultAsync(
                x => x.ResendOfNotificationId == notificationId && x.IdempotencyKey == normalizedKey,
                cancellationToken);
            if (existing is not null)
            {
                return existing.Id;
            }

            var original = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken)
                ?? throw new KeyNotFoundException("Notification not found.");
            if (original.ProviderMessageSid is not null)
            {
                await TryRefreshAsync(original, cancellationToken);
            }

            if (!CanResend(original.ProviderStatus))
            {
                throw new InvalidOperationException("Only a failed or undelivered notification can be resent.");
            }

            if (original.Body is null)
            {
                throw new InvalidOperationException("A redacted notification cannot be resent.");
            }

            var order = await _db.Orders.SingleAsync(x => x.Id == original.OrderId, cancellationToken);
            var originalType = await FindOriginalTypeAsync(original, cancellationToken);
            if (order.Status == OrderStatus.Cancelled && originalType != NotificationType.OrderCancelled)
            {
                throw new InvalidOperationException("Only the cancellation notice can be resent for a cancelled order.");
            }

            var contact = await _db.ContactNumbers.SingleOrDefaultAsync(
                x => x.Id == original.ContactNumberId && x.DeletedAt == null,
                cancellationToken);
            if (contact is null)
            {
                throw new InvalidOperationException("The destination contact number is no longer registered.");
            }

            var notification = new OrderNotification(
                original.OrderId,
                contact.Id,
                NotificationType.Resend,
                original.Body,
                _clock.GetUtcNow(),
                resendOfNotificationId: original.Id,
                idempotencyKey: normalizedKey);
            _db.OrderNotifications.Add(notification);
            await _db.SaveChangesAsync(cancellationToken); // claim the key before contacting Twilio

            await SendAndRecordAsync(notification, contact.PhoneNumber, null, cancellationToken);
            return notification.Id;
        }
        finally
        {
            gate.Release();
            if (gate.CurrentCount == 1)
            {
                ResendLocks.TryRemove(lockKey, out _);
            }
        }
    }

    public async Task RedactAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken)
            ?? throw new KeyNotFoundException("Notification not found.");
        if (notification.Body is null)
        {
            return;
        }

        if (notification.ProviderMessageSid is not null)
        {
            var state = await _twilio.RedactAsync(notification.ProviderMessageSid, cancellationToken);
            notification.RecordProviderState(state, _clock.GetUtcNow());
        }

        notification.Redact(_clock.GetUtcNow());
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task NotifyAllAsync(
        Order order,
        NotificationType type,
        string body,
        DateTimeOffset? scheduledFor,
        CancellationToken cancellationToken)
    {
        var contacts = await _db.ContactNumbers
            .Where(x => x.OwnerId == order.BuyerId && x.DeletedAt == null)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

        foreach (var contact in contacts)
        {
            var notification = new OrderNotification(order.Id, contact.Id, type, body, _clock.GetUtcNow(), scheduledFor);
            try
            {
                _db.OrderNotifications.Add(notification);
                await _db.SaveChangesAsync(CancellationToken.None);
                await SendAndRecordAsync(notification, contact.PhoneNumber, scheduledFor, cancellationToken);
            }
            catch (Exception exception)
            {
                notification.RecordFailure(GetErrorCode(exception), _clock.GetUtcNow());
                await TrySaveChangesAsync(CancellationToken.None);
                _logger.LogWarning("Notification {NotificationId} for order {OrderId} could not be sent. Provider error code: {ProviderErrorCode}",
                    notification.Id, order.Id, GetErrorCode(exception));
            }
        }
    }

    private async Task SendAndRecordAsync(
        OrderNotification notification,
        string phoneNumber,
        DateTimeOffset? scheduledFor,
        CancellationToken cancellationToken)
    {
        try
        {
            var state = scheduledFor.HasValue
                ? await _twilio.ScheduleAsync(phoneNumber, notification.Body!, scheduledFor.Value, cancellationToken)
                : await _twilio.SendAsync(phoneNumber, notification.Body!, cancellationToken);
            notification.RecordProviderState(state, _clock.GetUtcNow());
        }
        catch (Exception exception)
        {
            notification.RecordFailure(GetErrorCode(exception), _clock.GetUtcNow());
            _logger.LogWarning("Notification {NotificationId} for order {OrderId} could not be sent. Provider error code: {ProviderErrorCode}",
                notification.Id, notification.OrderId, GetErrorCode(exception));
        }

        await TrySaveChangesAsync(CancellationToken.None);
    }

    private async Task RequestCancellationAsync(List<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            notification.RequestCancellation();
        }
        await TrySaveChangesAsync(CancellationToken.None);

        foreach (var notification in notifications)
        {
            await TryCancelAsync(notification, cancellationToken);
        }
    }

    private async Task TryCancelAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (notification.ProviderMessageSid is null)
        {
            return;
        }

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var state = await _twilio.CancelAsync(notification.ProviderMessageSid, cancellationToken);
                notification.RecordCancellation(state, _clock.GetUtcNow());
                await TrySaveChangesAsync(CancellationToken.None);
                return;
            }
            catch (Exception exception) when (attempt < 3 && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Scheduled notification {NotificationId} cancellation attempt {Attempt} failed. Provider error code: {ProviderErrorCode}",
                    notification.Id, attempt, GetErrorCode(exception));
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogError("Scheduled notification {NotificationId} remains pending cancellation. Provider error code: {ProviderErrorCode}",
                    notification.Id, GetErrorCode(exception));
                return;
            }
        }
    }

    private async Task TryRefreshAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            var state = await _twilio.FetchAsync(notification.ProviderMessageSid!, cancellationToken);
            notification.RecordProviderState(state, _clock.GetUtcNow());
        }
        catch (Exception exception)
        {
            _logger.LogWarning("Notification {NotificationId} status could not be refreshed. Provider error code: {ProviderErrorCode}",
                notification.Id, GetErrorCode(exception));
        }
    }

    private async Task TrySaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Notification state could not be persisted.");
        }
    }

    private async Task<NotificationType> FindOriginalTypeAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        var current = notification;
        while (current.Type == NotificationType.Resend && current.ResendOfNotificationId.HasValue)
        {
            current = await _db.OrderNotifications.SingleAsync(
                x => x.Id == current.ResendOfNotificationId.Value,
                cancellationToken);
        }

        return current.Type;
    }

    private static bool CanResend(string status) => status is "failed" or "undelivered";
    private static int? GetErrorCode(Exception exception) => exception is TwilioApiException twilioException ? twilioException.ErrorCode : null;
}
