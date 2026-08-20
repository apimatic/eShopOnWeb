using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
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
    private readonly IMessagingProviderSettings _providerSettings;
    private readonly ITrackedNotificationStore _trackedNotifications;
    private readonly INotificationRedactionState _redactionState;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notifications,
        IRepository<ContactNumber> contactNumbers,
        ITwilioMessagingClient messagingClient,
        IMessagingProviderSettings providerSettings,
        ITrackedNotificationStore trackedNotifications,
        INotificationRedactionState redactionState,
        IAppLogger<OrderNotificationService> logger)
    {
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _messagingClient = messagingClient;
        _providerSettings = providerSettings;
        _trackedNotifications = trackedNotifications;
        _redactionState = redactionState;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
        => NotifyDestinationsAsync(
            order,
            OrderNotificationKind.OrderPlaced,
            $"Your eShop order #{order.Id} has been placed. Thank you for your purchase.",
            sendAt: null,
            cancellationToken);

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        await NotifyDestinationsAsync(
            order,
            OrderNotificationKind.OrderDispatched,
            $"Your eShop order #{order.Id} is on its way.",
            sendAt: null,
            cancellationToken);

        await NotifyDestinationsAsync(
            order,
            OrderNotificationKind.DeliveryFollowUp,
            $"How did the delivery of your eShop order #{order.Id} go? We would love to hear from you.",
            sendAt: DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay),
            cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        await CancelFollowUpsForOrderAsync(order.Id, cancellationToken);

        await NotifyDestinationsAsync(
            order,
            OrderNotificationKind.OrderCancelled,
            $"Your eShop order #{order.Id} has been cancelled.",
            sendAt: null,
            cancellationToken);
    }

    public async Task CancelScheduledForDestinationAsync(string destination, CancellationToken cancellationToken = default)
    {
        var spec = new CancellableFollowUpsByDestinationSpecification(destination);
        var followUps = await _notifications.ListAsync(spec, cancellationToken);
        foreach (var followUp in followUps.Where(f => f.IsCancellableFollowUp()))
        {
            await CancelFollowUpAsync(followUp, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var spec = new OrderNotificationsByOrderIdSpecification(orderId);
        var notifications = await _notifications.ListAsync(spec, cancellationToken);
        ApplyRedactionState(notifications);
        await RefreshStatusesAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrdersAsync(IEnumerable<int> orderIds, CancellationToken cancellationToken = default)
    {
        var ids = orderIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return Array.Empty<OrderNotification>();
        }

        var spec = new OrderNotificationsByOrderIdsSpecification(ids);
        var notifications = await _notifications.ListAsync(spec, cancellationToken);
        ApplyRedactionState(notifications);
        await RefreshStatusesAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new OrderNotificationException("An idempotency key is required.");
        }

        var existing = await _notifications.FirstOrDefaultAsync(
            new OrderNotificationByResendKeySpecification(notificationId, idempotencyKey.Trim()),
            cancellationToken);
        if (existing is not null)
        {
            await RefreshStatusesAsync(new[] { existing }, cancellationToken);
            return existing;
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
        {
            throw new OrderNotificationException("Notification was not found.", 404);
        }

        if (original.ContentRedacted
            || _redactionState.IsRedacted(original.Id)
            || string.IsNullOrWhiteSpace(original.Body))
        {
            throw new OrderNotificationException("The original message content is no longer available to resend.", 409);
        }

        await RefreshStatusesAsync(new[] { original }, cancellationToken);
        if (!original.DidNotReachShopper())
        {
            throw new OrderNotificationException("Only messages that did not reach the shopper can be resent.", 409);
        }

        var destinationStillRegistered = await DestinationStillRegisteredAsync(original, cancellationToken);
        if (!destinationStillRegistered)
        {
            throw new OrderNotificationException("The destination number is no longer on file and cannot be messaged again.", 409);
        }

        var resent = new OrderNotification(
            original.OrderId,
            original.BuyerId,
            original.Kind,
            original.Body!,
            original.ContactNumberId,
            original.Destination,
            scheduledSendAt: null,
            resentFromNotificationId: original.Id,
            idempotencyKey: idempotencyKey.Trim());

        await _notifications.AddAsync(resent, cancellationToken);
        await TrySendAsync(resent, sendAt: null, cancellationToken);
        return resent;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _trackedNotifications.GetTrackedAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            throw new OrderNotificationException("Notification was not found.", 404);
        }

        if (!string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
        {
            try
            {
                var provider = await _messagingClient.RedactMessageBodyAsync(notification.ProviderMessageSid, cancellationToken);
                notification.ApplyProviderResult(provider.Sid, provider.Status, provider.ErrorCode, provider.ErrorMessage);
            }
            catch (Exception)
            {
                _logger.LogWarning("Failed to redact provider content for notification {NotificationId}.", notification.Id);
                throw new OrderNotificationException("The messaging provider could not dispose of the message content.", 502);
            }
        }

        notification.RedactContent();
        await _trackedNotifications.SaveRedactionAsync(notification, cancellationToken);
        _logger.LogWarning("Disposed message content for notification {NotificationId} on order {OrderId}.", notification.Id, notification.OrderId);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new OrderNotificationException("'to' must be on or after 'from'.");
        }

        var fromNumber = _providerSettings.FromNumber;
        if (string.IsNullOrWhiteSpace(fromNumber))
        {
            throw new OrderNotificationException("Twilio:FromNumber is not configured.", 500);
        }

        var providerMessages = await _messagingClient.ListMessagesFromAsync(fromNumber, from, to, cancellationToken);
        var applicationNotifications = await _notifications.ListAsync(
            new OrderNotificationsByCreatedRangeSpecification(from, to),
            cancellationToken);

        var applicationBySid = applicationNotifications
            .Where(n => !string.IsNullOrWhiteSpace(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrWhiteSpace(m.Sid))
            .GroupBy(m => m.Sid, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        var applicationOnly = new List<ReconciliationEntry>();

        foreach (var provider in providerMessages)
        {
            if (applicationBySid.TryGetValue(provider.Sid, out var local))
            {
                matched.Add(new ReconciliationEntry(
                    local.Id.ToString(),
                    provider.Sid,
                    provider.Status,
                    local.Kind.ToString(),
                    "matched"));
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry(
                    null,
                    provider.Sid,
                    provider.Status,
                    null,
                    "providerOnly"));
            }
        }

        foreach (var local in applicationNotifications)
        {
            if (string.IsNullOrWhiteSpace(local.ProviderMessageSid)
                || !providerBySid.ContainsKey(local.ProviderMessageSid))
            {
                applicationOnly.Add(new ReconciliationEntry(
                    local.Id.ToString(),
                    local.ProviderMessageSid,
                    local.ProviderStatus,
                    local.Kind.ToString(),
                    "applicationOnly"));
            }
        }

        return new ReconciliationReport(
            from,
            to,
            fromNumber,
            providerMessages.Count,
            applicationNotifications.Count,
            matched,
            providerOnly,
            applicationOnly);
    }

    private async Task NotifyDestinationsAsync(
        Order order,
        OrderNotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        var destinations = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
        if (destinations.Count == 0)
        {
            _logger.LogInformation("No contact number on file for order {OrderId}; skipping {Kind} notification.", order.Id, kind);
            return;
        }

        foreach (var destination in destinations)
        {
            var notification = new OrderNotification(
                order.Id,
                order.BuyerId,
                kind,
                body,
                destination.Id,
                destination.PhoneNumber,
                sendAt);

            await _notifications.AddAsync(notification, cancellationToken);
            await TrySendAsync(notification, sendAt, cancellationToken);
        }
    }

    private async Task TrySendAsync(OrderNotification notification, DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        try
        {
            var provider = await _messagingClient.SendMessageAsync(
                new SendProviderMessageRequest(notification.Destination, notification.Body ?? string.Empty, sendAt),
                cancellationToken);
            notification.ApplyProviderResult(provider.Sid, provider.Status, provider.ErrorCode, provider.ErrorMessage);
            _logger.LogInformation(
                "Recorded provider message {MessageSid} for notification {NotificationId} on order {OrderId} with status {Status}.",
                provider.Sid,
                notification.Id,
                notification.OrderId,
                provider.Status ?? string.Empty);
        }
        catch (Exception)
        {
            notification.MarkSendFailed("The messaging provider could not accept the message.");
            _logger.LogWarning(
                "Failed to send notification {NotificationId} for order {OrderId}. The order operation still succeeded.",
                notification.Id,
                notification.OrderId);
        }

        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    private async Task CancelFollowUpsForOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderIdSpecification(orderId), cancellationToken);
        foreach (var followUp in notifications.Where(n => n.IsCancellableFollowUp()))
        {
            await CancelFollowUpAsync(followUp, cancellationToken);
        }
    }

    private async Task CancelFollowUpAsync(OrderNotification followUp, CancellationToken cancellationToken)
    {
        try
        {
            var provider = await _messagingClient.CancelMessageAsync(followUp.ProviderMessageSid!, cancellationToken);
            followUp.ApplyProviderResult(provider.Sid, provider.Status, provider.ErrorCode, provider.ErrorMessage);
            _logger.LogInformation(
                "Cancelled provider message {MessageSid} for notification {NotificationId} on order {OrderId}.",
                followUp.ProviderMessageSid ?? string.Empty,
                followUp.Id,
                followUp.OrderId);
        }
        catch (Exception)
        {
            _logger.LogWarning(
                "Failed to cancel scheduled follow-up {NotificationId} for order {OrderId}.",
                followUp.Id,
                followUp.OrderId);
        }

        await _notifications.UpdateAsync(followUp, cancellationToken);
    }

    private void ApplyRedactionState(IEnumerable<OrderNotification> notifications)
    {
        foreach (var notification in notifications)
        {
            if (notification.ContentRedacted || _redactionState.IsRedacted(notification.Id))
            {
                notification.RedactContent();
            }
        }
    }

    private async Task RefreshStatusesAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (notification.ContentRedacted
                || string.IsNullOrWhiteSpace(notification.ProviderMessageSid)
                || notification.IsTerminalProviderStatus())
            {
                continue;
            }

            try
            {
                var provider = await _messagingClient.FetchMessageAsync(notification.ProviderMessageSid, cancellationToken);
                notification.ApplyProviderResult(provider.Sid, provider.Status, provider.ErrorCode, provider.ErrorMessage);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception)
            {
                _logger.LogWarning(
                    "Failed to refresh provider status for notification {NotificationId} on order {OrderId}.",
                    notification.Id,
                    notification.OrderId);
            }
        }
    }

    private async Task<bool> DestinationStillRegisteredAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (notification.ContactNumberId.HasValue)
        {
            var byId = await _contactNumbers.GetByIdAsync(notification.ContactNumberId.Value, cancellationToken);
            if (byId is not null && byId.BuyerId == notification.BuyerId)
            {
                return true;
            }
        }

        var byNumber = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndPhoneSpecification(notification.BuyerId, notification.Destination),
            cancellationToken);
        return byNumber is not null;
    }
}
