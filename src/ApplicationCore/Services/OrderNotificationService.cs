using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    private static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<NotificationResendIdempotency> _resendIdempotency;
    private readonly ITwilioMessagingClient _messaging;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        IRepository<NotificationResendIdempotency> resendIdempotency,
        ITwilioMessagingClient messaging,
        IAppLogger<OrderNotificationService> logger)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _resendIdempotency = resendIdempotency;
        _messaging = messaging;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
        => NotifyAsync(order, OrderNotificationKind.OrderPlaced, scheduleFollowUp: false, cancellationToken);

    public Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
        => NotifyAsync(order, OrderNotificationKind.OrderDispatched, scheduleFollowUp: true, cancellationToken);

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);
        await NotifyAsync(order, OrderNotificationKind.OrderCancelled, scheduleFollowUp: false, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> GetAndRefreshForOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderIdSpecification(orderId), cancellationToken);
        await RefreshProviderStateAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<IReadOnlyList<OrderNotification>> GetAndRefreshForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notifications.ListAsync(new OrderNotificationsByBuyerSpecification(buyerId), cancellationToken);
        await RefreshProviderStateAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<ResendNotificationResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var existing = await _resendIdempotency.FirstOrDefaultAsync(
            new ResendIdempotencySpecification(notificationId, idempotencyKey), cancellationToken);
        if (existing != null)
        {
            return new ResendNotificationResult
            {
                Success = true,
                NotificationId = existing.ResultNotificationId
            };
        }

        var source = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (source == null)
        {
            return new ResendNotificationResult { NotFound = true, Error = "Notification was not found." };
        }

        var destinationStillOnFile = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndE164Specification(source.BuyerId, source.DestinationE164),
            cancellationToken);
        if (destinationStillOnFile == null)
        {
            return new ResendNotificationResult
            {
                DestinationNoLongerRegistered = true,
                Error = "The destination number is no longer on file."
            };
        }

        var body = OrderNotificationMessages.For(source.Kind, source.OrderId);
        var resent = new OrderNotification(source.OrderId, source.BuyerId, source.DestinationE164, source.Kind, body);
        resent.MarkAsResendOf(source.Id);

        try
        {
            var snapshot = await _messaging.SendAsync(source.DestinationE164, body, cancellationToken);
            ApplySnapshot(resent, snapshot);
        }
        catch (TwilioMessagingException)
        {
            _logger.LogWarning("Resend failed for notification {NotificationId} of order {OrderId}.", source.Id, source.OrderId);
            resent.MarkSendFailed("send_failed");
        }

        await _notifications.AddAsync(resent, cancellationToken);

        var idempotency = new NotificationResendIdempotency(source.Id, idempotencyKey, resent.Id);
        try
        {
            await _resendIdempotency.AddAsync(idempotency, cancellationToken);
        }
        catch (Exception)
        {
            var raced = await _resendIdempotency.FirstOrDefaultAsync(
                new ResendIdempotencySpecification(notificationId, idempotencyKey), cancellationToken);
            if (raced != null)
            {
                return new ResendNotificationResult
                {
                    Success = true,
                    NotificationId = raced.ResultNotificationId
                };
            }

            throw;
        }

        return new ResendNotificationResult
        {
            Success = true,
            NotificationId = resent.Id
        };
    }

    public async Task<RedactNotificationResult> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification == null)
        {
            return new RedactNotificationResult { NotFound = true, Error = "Notification was not found." };
        }

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            try
            {
                var snapshot = await _messaging.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
                ApplySnapshot(notification, snapshot);
            }
            catch (TwilioMessagingException)
            {
                _logger.LogWarning("Provider content redaction failed for notification {NotificationId}.", notification.Id);
                return new RedactNotificationResult { Error = "The provider could not redact the message content." };
            }
        }

        notification.RedactContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
        return new RedactNotificationResult { Success = true };
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var providerMessages = await _messaging.ListFromConfiguredNumberAsync(from, to, cancellationToken);
        var eShopNotifications = await _notifications.ListAsync(new OrderNotificationsInRangeSpecification(from, to), cancellationToken);

        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid)
            .ToDictionary(g => g.Key, g => g.First());

        var eShopBySid = eShopNotifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        var eShopOnly = new List<ReconciliationEntry>();

        foreach (var pair in providerBySid)
        {
            if (eShopBySid.TryGetValue(pair.Key, out var ours))
            {
                matched.Add(new ReconciliationEntry
                {
                    ProviderMessageSid = pair.Key,
                    NotificationId = ours.Id,
                    ProviderStatus = pair.Value.Status,
                    EShopStatus = ours.ProviderStatus,
                    DateSent = pair.Value.DateSent
                });
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry
                {
                    ProviderMessageSid = pair.Key,
                    ProviderStatus = pair.Value.Status,
                    DateSent = pair.Value.DateSent
                });
            }
        }

        foreach (var pair in eShopBySid)
        {
            if (!providerBySid.ContainsKey(pair.Key))
            {
                eShopOnly.Add(new ReconciliationEntry
                {
                    ProviderMessageSid = pair.Key,
                    NotificationId = pair.Value.Id,
                    EShopStatus = pair.Value.ProviderStatus,
                    DateSent = pair.Value.ProviderDateSent
                });
            }
        }

        return new NotificationReconciliationReport
        {
            From = from,
            To = to,
            Matched = matched,
            ProviderOnly = providerOnly,
            EShopOnly = eShopOnly
        };
    }

    private async Task NotifyAsync(Order order, OrderNotificationKind kind, bool scheduleFollowUp, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
        if (numbers.Count == 0)
        {
            return;
        }

        foreach (var number in numbers)
        {
            await TrySendAsync(order, number.E164Number, kind, cancellationToken);
            if (scheduleFollowUp)
            {
                await TryScheduleFollowUpAsync(order, number.E164Number, cancellationToken);
            }
        }
    }

    private async Task TrySendAsync(Order order, string destinationE164, OrderNotificationKind kind, CancellationToken cancellationToken)
    {
        var body = OrderNotificationMessages.For(kind, order.Id);
        var notification = new OrderNotification(order.Id, order.BuyerId, destinationE164, kind, body);

        try
        {
            var snapshot = await _messaging.SendAsync(destinationE164, body, cancellationToken);
            ApplySnapshot(notification, snapshot);
        }
        catch (TwilioMessagingException)
        {
            _logger.LogWarning("Immediate notification failed for order {OrderId} kind {Kind}.", order.Id, kind);
            notification.MarkSendFailed("send_failed");
        }
        catch (Exception)
        {
            _logger.LogWarning("Immediate notification failed unexpectedly for order {OrderId} kind {Kind}.", order.Id, kind);
            notification.MarkSendFailed("send_failed");
        }

        await _notifications.AddAsync(notification, cancellationToken);
    }

    private async Task TryScheduleFollowUpAsync(Order order, string destinationE164, CancellationToken cancellationToken)
    {
        var body = OrderNotificationMessages.For(OrderNotificationKind.DeliveryFollowUp, order.Id);
        var notification = new OrderNotification(
            order.Id, order.BuyerId, destinationE164, OrderNotificationKind.DeliveryFollowUp, body);
        var sendAt = DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay);

        try
        {
            var snapshot = await _messaging.ScheduleAsync(destinationE164, body, sendAt, cancellationToken);
            ApplySnapshot(notification, snapshot);
        }
        catch (TwilioMessagingException)
        {
            _logger.LogWarning("Follow-up scheduling failed for order {OrderId}.", order.Id);
            notification.MarkSendFailed("schedule_failed");
        }
        catch (Exception)
        {
            _logger.LogWarning("Follow-up scheduling failed unexpectedly for order {OrderId}.", order.Id);
            notification.MarkSendFailed("schedule_failed");
        }

        await _notifications.AddAsync(notification, cancellationToken);
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var followUps = await _notifications.ListAsync(new ScheduledFollowUpsByOrderSpecification(orderId), cancellationToken);
        foreach (var followUp in followUps)
        {
            if (string.IsNullOrEmpty(followUp.ProviderMessageSid))
            {
                continue;
            }

            var status = followUp.ProviderStatus;
            if (!string.Equals(status, "scheduled", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var latest = await _messaging.FetchAsync(followUp.ProviderMessageSid, cancellationToken);
                    if (latest != null)
                    {
                        ApplySnapshot(followUp, latest);
                        status = latest.Status;
                    }
                }
                catch (TwilioMessagingException)
                {
                    _logger.LogWarning("Could not refresh follow-up status for order {OrderId} before cancel.", orderId);
                }
            }

            if (!string.Equals(status, "scheduled", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var canceled = await _messaging.CancelScheduledAsync(followUp.ProviderMessageSid, cancellationToken);
                ApplySnapshot(followUp, canceled);
            }
            catch (TwilioMessagingException)
            {
                _logger.LogWarning("Could not cancel a scheduled follow-up for order {OrderId}.", orderId);
            }

            await _notifications.UpdateAsync(followUp, cancellationToken);
        }
    }

    private async Task RefreshProviderStateAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var snapshot = await _messaging.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                if (snapshot == null)
                {
                    continue;
                }

                ApplySnapshot(notification, snapshot);
                if (notification.ContentRedacted)
                {
                    notification.RedactContent();
                }

                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (TwilioMessagingException)
            {
                _logger.LogWarning("Could not refresh provider status for notification {NotificationId}.", notification.Id);
            }
        }
    }

    private static void ApplySnapshot(OrderNotification notification, TwilioMessageSnapshot snapshot)
    {
        notification.ApplyProviderState(
            snapshot.Sid,
            snapshot.Status,
            snapshot.ErrorCode,
            snapshot.DateCreated,
            snapshot.DateSent);
    }
}
