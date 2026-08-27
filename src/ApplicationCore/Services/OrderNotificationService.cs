using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public sealed class OrderNotificationService : IOrderNotificationService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ResendLocks = new();
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly ITwilioGateway _twilio;
    private readonly IAppLogger<OrderNotificationService> _logger;
    private readonly TimeProvider _timeProvider;

    public OrderNotificationService(
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        ITwilioGateway twilio,
        IAppLogger<OrderNotificationService> logger,
        TimeProvider timeProvider)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _twilio = twilio;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken) =>
        SendToActiveContactsAsync(order, NotificationKind.OrderPlaced,
            $"Your eShopOnWeb order #{order.Id} has been placed.", null, cancellationToken);

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken)
    {
        await SendToActiveContactsAsync(order, NotificationKind.OrderDispatched,
            $"Your eShopOnWeb order #{order.Id} is on its way.", null, cancellationToken);
        var sendAt = _timeProvider.GetUtcNow().Add(FollowUpDelay);
        await SendToActiveContactsAsync(order, NotificationKind.DeliveryFollowUp,
            $"How did delivery of eShopOnWeb order #{order.Id} go?", sendAt, cancellationToken);
    }

    public Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken) =>
        SendToActiveContactsAsync(order, NotificationKind.OrderCancelled,
            $"Your eShopOnWeb order #{order.Id} has been cancelled.", null, cancellationToken);

    public async Task CancelPendingFollowUpsForOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var pending = await _notifications.ListAsync(new PendingFollowUpsByOrderSpec(orderId), cancellationToken);
        await CancelPendingFollowUpsAsync(pending, cancellationToken);
    }

    public async Task CancelPendingFollowUpsForContactAsync(int contactNumberId,
        CancellationToken cancellationToken)
    {
        var pending = await _notifications.ListAsync(new PendingFollowUpsByContactSpec(contactNumberId),
            cancellationToken);
        await CancelPendingFollowUpsAsync(pending, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> GetCurrentNotificationsAsync(int orderId,
        CancellationToken cancellationToken)
    {
        var notifications = await _notifications.ListAsync(new NotificationsByOrderSpec(orderId), cancellationToken);
        foreach (var notification in notifications.Where(item => item.ProviderMessageSid != null))
        {
            await TryRefreshAsync(notification, cancellationToken);
        }

        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 128)
        {
            throw new NotificationOperationException("An idempotency key of 1 to 128 characters is required.");
        }

        var normalizedKey = idempotencyKey.Trim();
        var lockKey = $"{notificationId}:{normalizedKey}";
        var gate = ResendLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var existing = await _notifications.FirstOrDefaultAsync(
                new ResendByKeySpec(notificationId, normalizedKey), cancellationToken);
            if (existing != null)
            {
                return existing;
            }

            var original = await _notifications.GetByIdAsync(notificationId, cancellationToken)
                ?? throw new NotificationOperationException("Notification was not found.");
            await TryRefreshAsync(original, cancellationToken);
            if (!NotificationDeliveryStatus.DidNotReach(original.ProviderStatus))
            {
                throw new NotificationOperationException("Only a notification that did not reach the shopper can be resent.");
            }

            if (string.IsNullOrEmpty(original.Content))
            {
                throw new NotificationOperationException("Disposed notification content cannot be resent.");
            }

            var contact = await _contactNumbers.GetByIdAsync(original.ContactNumberId, cancellationToken);
            if (contact == null || !contact.IsActive)
            {
                throw new NotificationOperationException("The destination contact number is no longer active.");
            }

            var now = _timeProvider.GetUtcNow();
            var resend = new OrderNotification(original.OrderId, contact.Id, original.BuyerId,
                NotificationKind.Resend, original.Content, now, resendOfNotificationId: original.Id,
                idempotencyKey: normalizedKey);
            resend = await _notifications.AddAsync(resend, cancellationToken);
            await TrySendAsync(resend, contact.CanonicalNumber, cancellationToken);
            return resend;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new NotificationOperationException("Notification was not found.");
        if (notification.ContentDisposedAt.HasValue)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
        {
            var providerMessage = await _twilio.RedactMessageContentAsync(notification.ProviderMessageSid,
                cancellationToken);
            if (!string.IsNullOrEmpty(providerMessage.Body))
            {
                throw new NotificationOperationException("The provider did not confirm content disposal.");
            }

            ApplyProviderState(notification, providerMessage);
        }

        notification.DisposeContent(_timeProvider.GetUtcNow());
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    private async Task SendToActiveContactsAsync(Order order, NotificationKind kind, string content,
        DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        IReadOnlyList<ContactNumber> contacts;
        try
        {
            contacts = await _contactNumbers.ListAsync(new ActiveContactNumbersByBuyerSpec(order.BuyerId),
                cancellationToken);
        }
        catch
        {
            _logger.LogWarning("Could not load contacts for order {OrderId}; the order operation remains successful.",
                order.Id);
            return;
        }

        foreach (var contact in contacts)
        {
            try
            {
                var now = _timeProvider.GetUtcNow();
                var notification = new OrderNotification(order.Id, contact.Id, order.BuyerId, kind, content,
                    now, sendAt);
                notification = await _notifications.AddAsync(notification, cancellationToken);
                await TrySendAsync(notification, contact.CanonicalNumber, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                _logger.LogWarning("Notification persistence failed for order {OrderId}; the order operation remains successful.",
                    order.Id);
            }
        }
    }

    private async Task TrySendAsync(OrderNotification notification, string destination,
        CancellationToken cancellationToken)
    {
        try
        {
            var providerMessage = await _twilio.SendMessageAsync(destination, notification.Content!,
                notification.ScheduledFor, cancellationToken);
            ApplyProviderState(notification, providerMessage);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TwilioRequestException exception)
        {
            notification.RecordProviderFailure(exception.ProviderCode, _timeProvider.GetUtcNow());
            _logger.LogWarning("Twilio rejected notification {NotificationId}; the order operation remains successful.",
                notification.Id);
        }
        catch
        {
            notification.RecordProviderFailure(null, _timeProvider.GetUtcNow());
            _logger.LogWarning("Twilio could not send notification {NotificationId}; the order operation remains successful.",
                notification.Id);
        }

        try
        {
            await _notifications.UpdateAsync(notification, cancellationToken);
        }
        catch
        {
            _logger.LogWarning("Could not persist provider state for notification {NotificationId}.", notification.Id);
        }
    }

    private async Task TryRefreshAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            var providerMessage = await _twilio.FetchMessageAsync(notification.ProviderMessageSid!, cancellationToken);
            ApplyProviderState(notification, providerMessage);
            await _notifications.UpdateAsync(notification, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            _logger.LogWarning("Could not refresh provider state for notification {NotificationId}.", notification.Id);
        }
    }

    private async Task CancelPendingFollowUpsAsync(IReadOnlyList<OrderNotification> pending,
        CancellationToken cancellationToken)
    {
        foreach (var notification in pending)
        {
            try
            {
                var current = await _twilio.FetchMessageAsync(notification.ProviderMessageSid!, cancellationToken);
                ApplyProviderState(notification, current);
                if (NotificationDeliveryStatus.MayStillBeSent(current.Status))
                {
                    current = await _twilio.CancelMessageAsync(notification.ProviderMessageSid!, cancellationToken);
                    ApplyProviderState(notification, current);
                }

                if (current.Status is not ("canceled" or "failed" or "undelivered"))
                {
                    throw new FollowUpCancellationException(notification.Id);
                }

                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (FollowUpCancellationException)
            {
                throw;
            }
            catch
            {
                throw new FollowUpCancellationException(notification.Id);
            }
        }
    }

    private void ApplyProviderState(OrderNotification notification, ProviderMessage providerMessage)
    {
        notification.RecordProviderState(providerMessage.Sid, providerMessage.Status,
            providerMessage.ErrorCode, providerMessage.DateSent, _timeProvider.GetUtcNow());
    }
}
