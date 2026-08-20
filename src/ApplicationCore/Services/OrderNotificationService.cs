using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ITwilioMessagingClient _messagingClient;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notifications,
        IRepository<ContactNumber> contactNumbers,
        ITwilioMessagingClient messagingClient,
        IAppLogger<OrderNotificationService> logger)
    {
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _messagingClient = messagingClient;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        return NotifyAsync(
            order,
            OrderNotificationType.OrderPlaced,
            $"Your eShop order #{order.Id} has been placed.",
            sendAt: null,
            cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        await NotifyAsync(
            order,
            OrderNotificationType.OrderDispatched,
            $"Your eShop order #{order.Id} is on its way.",
            sendAt: null,
            cancellationToken);

        await NotifyAsync(
            order,
            OrderNotificationType.DeliveryFollowUp,
            $"How did the delivery of eShop order #{order.Id} go?",
            sendAt: DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay),
            cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        await CancelOutstandingFollowUpsAsync(order.Id, cancellationToken);

        await NotifyAsync(
            order,
            OrderNotificationType.OrderCancelled,
            $"Your eShop order #{order.Id} has been cancelled.",
            sendAt: null,
            cancellationToken);
    }

    public async Task CancelOutstandingMessagesForNumberAsync(string buyerId, string destinationNumber, CancellationToken cancellationToken = default)
    {
        var scheduled = await _notifications.ListAsync(
            new ScheduledNotificationsByDestinationSpecification(buyerId, destinationNumber), cancellationToken);

        foreach (var notification in scheduled)
        {
            await CancelIfStillScheduledAsync(notification, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notifications.ListAsync(new NotificationsByOrderIdSpecification(orderId), cancellationToken);
        await SyncWithProviderAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrdersAsync(IReadOnlyCollection<int> orderIds, CancellationToken cancellationToken = default)
    {
        if (orderIds.Count == 0)
        {
            return Array.Empty<OrderNotification>();
        }

        var notifications = await _notifications.ListAsync(new NotificationsByOrderIdsSpecification(orderIds), cancellationToken);
        await SyncWithProviderAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.");
        }

        var existing = await _notifications.FirstOrDefaultAsync(
            new NotificationResendByIdempotencySpecification(notificationId, idempotencyKey), cancellationToken);
        if (existing is not null)
        {
            await SyncWithProviderAsync(new[] { existing }, cancellationToken);
            return existing;
        }

        var original = await _notifications.FirstOrDefaultAsync(
            new NotificationByIdSpecification(notificationId), cancellationToken);
        if (original is null)
        {
            throw new EntityNotFoundException("Notification not found.");
        }

        await SyncWithProviderAsync(new[] { original }, cancellationToken);

        if (!original.CanResend())
        {
            throw new NotificationOperationException("This message cannot be re-sent.");
        }

        var stillRegistered = await DestinationStillRegisteredAsync(original.BuyerId, original.DestinationNumber, original.ContactNumberId, cancellationToken);
        if (!stillRegistered)
        {
            throw new NotificationOperationException("The destination is no longer registered and cannot be messaged.");
        }

        var resend = new OrderNotification(
            original.OrderId,
            original.BuyerId,
            original.ContactNumberId,
            original.DestinationNumber,
            original.Type,
            original.Body!,
            scheduledAt: null,
            originalNotificationId: original.Id,
            idempotencyKey: idempotencyKey);

        await SendAndPersistAsync(resend, sendAt: null, cancellationToken);
        return resend;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.FirstOrDefaultAsync(
            new NotificationByIdSpecification(notificationId), cancellationToken);
        if (notification is null)
        {
            throw new EntityNotFoundException("Notification not found.");
        }

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            try
            {
                await _messagingClient.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("Failed to redact provider content for notification {NotificationId}: {Error}", notification.Id, ex.GetType().Name);
                throw;
            }
        }

        notification.MarkContentRedacted();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("The 'to' timestamp must be on or after 'from'.");
        }

        var fromNumber = _messagingClient.FromNumber;
        var providerMessages = await _messagingClient.ListSentFromAsync(fromNumber, from, to, cancellationToken);
        var applicationNotifications = await _notifications.ListAsync(new NotificationsCreatedBetweenSpecification(from, to), cancellationToken);

        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var applicationBySid = applicationNotifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var matched = new List<ReconciledNotification>();
        var providerOnly = new List<ProviderMessageRecord>();
        var applicationOnly = new List<ApplicationNotificationRecord>();

        foreach (var pair in providerBySid)
        {
            if (applicationBySid.TryGetValue(pair.Key, out var local))
            {
                matched.Add(new ReconciledNotification
                {
                    NotificationId = local.Id,
                    ProviderMessageSid = pair.Key,
                    ApplicationStatus = local.ProviderStatus,
                    ProviderStatus = pair.Value.Status
                });
            }
            else
            {
                providerOnly.Add(new ProviderMessageRecord
                {
                    ProviderMessageSid = pair.Key,
                    Status = pair.Value.Status,
                    DateSent = pair.Value.DateSent
                });
            }
        }

        foreach (var notification in applicationNotifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid) || !providerBySid.ContainsKey(notification.ProviderMessageSid))
            {
                applicationOnly.Add(new ApplicationNotificationRecord
                {
                    NotificationId = notification.Id,
                    ProviderMessageSid = notification.ProviderMessageSid,
                    Status = notification.ProviderStatus,
                    CreatedAt = notification.CreatedAt
                });
            }
        }

        return new NotificationReconciliationReport
        {
            From = from,
            To = to,
            FromNumber = fromNumber,
            Matched = matched,
            ProviderOnly = providerOnly,
            ApplicationOnly = applicationOnly
        };
    }

    private async Task NotifyAsync(
        Order order,
        OrderNotificationType type,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var destinations = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
            if (destinations.Count == 0)
            {
                return;
            }

            foreach (var destination in destinations)
            {
                var notification = new OrderNotification(
                    order.Id,
                    order.BuyerId,
                    destination.Id,
                    destination.CanonicalNumber,
                    type,
                    body,
                    sendAt);

                await SendAndPersistAsync(notification, sendAt, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Order {OrderId} notification {NotificationType} did not complete: {Error}", order.Id, type, ex.GetType().Name);
        }
    }

    private async Task SendAndPersistAsync(OrderNotification notification, DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _messagingClient.SendAsync(notification.DestinationNumber, notification.Body ?? string.Empty, sendAt, cancellationToken);
            notification.RecordProviderAccepted(snapshot.Sid, snapshot.Status, snapshot.DateSent, snapshot.ErrorCode, snapshot.ErrorMessage);
        }
        catch (TwilioApiException ex)
        {
            _logger.LogWarning("Provider rejected notification {NotificationId} for order {OrderId} with code {ErrorCode}", notification.Id, notification.OrderId, ex.ErrorCode ?? 0);
            notification.RecordSendFailure("failed", ex.ErrorCode, "The provider rejected the message.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Failed to send notification for order {OrderId}: {Error}", notification.OrderId, ex.GetType().Name);
            notification.RecordSendFailure("failed", null, "The message could not be sent.");
        }

        if (notification.Id == 0)
        {
            await _notifications.AddAsync(notification, cancellationToken);
        }
        else
        {
            await _notifications.UpdateAsync(notification, cancellationToken);
        }
    }

    private async Task CancelOutstandingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var followUps = await _notifications.ListAsync(new ScheduledFollowUpsByOrderSpecification(orderId), cancellationToken);
        foreach (var followUp in followUps)
        {
            await CancelIfStillScheduledAsync(followUp, cancellationToken);
        }
    }

    private async Task CancelIfStillScheduledAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            return;
        }

        try
        {
            var current = await _messagingClient.FetchAsync(notification.ProviderMessageSid, cancellationToken);
            notification.ApplyProviderState(current.Status, current.DateSent, current.ErrorCode, current.ErrorMessage);

            if (string.Equals(current.Status, "scheduled", StringComparison.OrdinalIgnoreCase))
            {
                var cancelled = await _messagingClient.CancelScheduledAsync(notification.ProviderMessageSid, cancellationToken);
                notification.ApplyProviderState(cancelled.Status, cancelled.DateSent, cancelled.ErrorCode, cancelled.ErrorMessage);
            }

            await _notifications.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Failed to cancel scheduled notification {NotificationId}: {Error}", notification.Id, ex.GetType().Name);
        }
    }

    private async Task SyncWithProviderAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var snapshot = await _messagingClient.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                notification.ApplyProviderState(snapshot.Status, snapshot.DateSent, snapshot.ErrorCode, snapshot.ErrorMessage);
                if (notification.ContentRedacted || snapshot.Body == string.Empty)
                {
                    notification.MarkContentRedacted();
                }
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("Failed to refresh provider status for notification {NotificationId}: {Error}", notification.Id, ex.GetType().Name);
            }
        }
    }

    private async Task<bool> DestinationStillRegisteredAsync(string buyerId, string destinationNumber, int? contactNumberId, CancellationToken cancellationToken)
    {
        if (contactNumberId.HasValue)
        {
            var byId = await _contactNumbers.FirstOrDefaultAsync(
                new ContactNumberByBuyerAndIdSpecification(buyerId, contactNumberId.Value), cancellationToken);
            if (byId is not null)
            {
                return true;
            }
        }

        var byNumber = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndCanonicalSpecification(buyerId, destinationNumber), cancellationToken);
        return byNumber is not null;
    }
}
