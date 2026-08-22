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

    private static readonly HashSet<string> TerminalFailureStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "failed", "undelivered", NotSentOrUnknown };

    private const string NotSentOrUnknown = "not_sent";

    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ITwilioMessagingClient _messaging;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notifications,
        IRepository<ContactNumber> contactNumbers,
        ITwilioMessagingClient messaging,
        IAppLogger<OrderNotificationService> logger)
    {
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _messaging = messaging;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"Your eShopOnWeb order #{order.Id} has been placed. Total: {order.Total():0.00}.";
        return NotifyAsync(order, NotificationKind.OrderPlaced, body, sendAt: null, cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var dispatchedBody = $"Your eShopOnWeb order #{order.Id} is on its way.";
        await NotifyAsync(order, NotificationKind.OrderDispatched, dispatchedBody, sendAt: null, cancellationToken);

        var followUpBody = $"How did the delivery of your eShopOnWeb order #{order.Id} go?";
        var sendAt = DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay);
        await NotifyAsync(order, NotificationKind.DeliveryFollowUp, followUpBody, sendAt, cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        await CancelOutstandingFollowUpsAsync(order.Id, cancellationToken);

        var body = $"Your eShopOnWeb order #{order.Id} has been cancelled.";
        await NotifyAsync(order, NotificationKind.OrderCancelled, body, sendAt: null, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notifications.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshFromProviderAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrdersAsync(IEnumerable<int> orderIds, CancellationToken cancellationToken = default)
    {
        var ids = orderIds.ToArray();
        if (ids.Length == 0)
        {
            return Array.Empty<OrderNotification>();
        }

        var notifications = await _notifications.ListAsync(new NotificationsByOrdersSpecification(ids), cancellationToken);
        await RefreshFromProviderAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.");
        }

        var existing = await _notifications.FirstOrDefaultAsync(
            new NotificationByParentAndIdempotencyKeySpecification(notificationId, idempotencyKey), cancellationToken);
        if (existing is not null)
        {
            await RefreshFromProviderAsync(new[] { existing }, cancellationToken);
            return existing;
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
        {
            throw new NotFoundException("Notification not found.");
        }

        await RefreshFromProviderAsync(new[] { original }, cancellationToken);

        if (original.ContentRedacted || string.IsNullOrWhiteSpace(original.Body))
        {
            throw new InvalidOrderOperationException("The message content has been disposed of and cannot be resent.");
        }

        if (!CanResend(original.ProviderStatus))
        {
            throw new InvalidOrderOperationException("Only messages that did not reach the shopper can be resent.");
        }

        var destinations = await ResolveActiveDestinationsAsync(original.BuyerId, original.ContactNumberId, cancellationToken);
        if (destinations.Count == 0)
        {
            throw new InvalidOrderOperationException("The original destination is no longer on file.");
        }

        var destination = destinations[0];
        var resend = new OrderNotification(
            original.OrderId,
            original.BuyerId,
            original.Kind,
            original.Body!,
            destination.Id,
            scheduledSendAt: null,
            parentNotificationId: original.Id,
            idempotencyKey: idempotencyKey);

        return await SendAndStoreAsync(resend, destination.PhoneNumber, sendAt: null, cancellationToken);
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            throw new NotFoundException("Notification not found.");
        }

        if (!string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
        {
            try
            {
                var redacted = await _messaging.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
                notification.ApplyProviderSnapshot(redacted.Status, redacted.ErrorCode, redacted.DateSent, body: null);
                if (!string.IsNullOrEmpty(redacted.Body))
                {
                    _logger.LogWarning(
                        "Provider still returned message content after redact for notification {NotificationId}.",
                        notification.Id);
                }
            }
            catch (Exception)
            {
                _logger.LogWarning(
                    "Failed to redact provider content for notification {NotificationId} with provider sid present.",
                    notification.Id);
                throw;
            }
        }

        notification.RedactContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<IReadOnlyList<ReconciliationItem>> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var providerMessages = await _messaging.ListFromConfiguredNumberAsync(from, to, cancellationToken);
        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrWhiteSpace(m.Sid))
            .GroupBy(m => m.Sid, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var localInRange = await _notifications.ListAsync(
            new NotificationsCreatedBetweenSpecification(from, to), cancellationToken);

        if (providerBySid.Count > 0)
        {
            var matchingLocal = await _notifications.ListAsync(
                new NotificationsByProviderSidsSpecification(providerBySid.Keys), cancellationToken);
            foreach (var local in matchingLocal.Where(candidate => localInRange.All(n => n.Id != candidate.Id)))
            {
                localInRange.Add(local);
            }
        }

        var items = new List<ReconciliationItem>();
        var matchedSids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var local in localInRange)
        {
            if (!string.IsNullOrWhiteSpace(local.ProviderMessageSid) &&
                providerBySid.TryGetValue(local.ProviderMessageSid, out var provider))
            {
                matchedSids.Add(local.ProviderMessageSid);
                items.Add(new ReconciliationItem(local.ProviderMessageSid, local.Id, "matched", provider.Status));
            }
            else
            {
                items.Add(new ReconciliationItem(local.ProviderMessageSid, local.Id, "eshop_only", local.ProviderStatus));
            }
        }

        foreach (var provider in providerBySid.Values)
        {
            if (matchedSids.Contains(provider.Sid))
            {
                continue;
            }

            items.Add(new ReconciliationItem(provider.Sid, null, "provider_only", provider.Status));
        }

        return items;
    }

    private async Task NotifyAsync(
        Order order,
        NotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var destinations = await _contactNumbers.ListAsync(
                new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
            if (destinations.Count == 0)
            {
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
                    sendAt);

                await SendAndStoreAsync(notification, destination.PhoneNumber, sendAt, cancellationToken);
            }
        }
        catch (Exception)
        {
            _logger.LogWarning(
                "Order {OrderId} notification of kind {Kind} could not be completed. The order operation still succeeded.",
                order.Id,
                kind);
        }
    }

    private async Task<OrderNotification> SendAndStoreAsync(
        OrderNotification notification,
        string destination,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        if (notification.Id == 0)
        {
            notification = await _notifications.AddAsync(notification, cancellationToken);
        }

        try
        {
            var result = await _messaging.SendAsync(destination, notification.Body!, sendAt, cancellationToken);
            notification.ApplyProviderAcceptance(result.Sid, result.Status, result.ErrorCode, result.DateSent);
        }
        catch (Exception)
        {
            notification.MarkSendFailed();
            _logger.LogWarning(
                "Provider rejected or failed to accept notification kind {Kind} for order {OrderId}.",
                notification.Kind,
                notification.OrderId);
        }

        await _notifications.UpdateAsync(notification, cancellationToken);
        return notification;
    }

    private async Task CancelOutstandingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var followUps = await _notifications.ListAsync(new DeliveryFollowUpsByOrderSpecification(orderId), cancellationToken);
        foreach (var followUp in followUps)
        {
            if (string.IsNullOrWhiteSpace(followUp.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var result = await _messaging.CancelScheduledAsync(followUp.ProviderMessageSid, cancellationToken);
                followUp.ApplyProviderSnapshot(result.Status, result.ErrorCode, result.DateSent, result.Body);
                await _notifications.UpdateAsync(followUp, cancellationToken);
            }
            catch (Exception)
            {
                _logger.LogWarning(
                    "Could not cancel scheduled follow-up notification {NotificationId} for order {OrderId}.",
                    followUp.Id,
                    orderId);
            }
        }
    }

    private async Task RefreshFromProviderAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var snapshot = await _messaging.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                if (notification.ContentRedacted)
                {
                    notification.ApplyProviderSnapshot(snapshot.Status, snapshot.ErrorCode, snapshot.DateSent, body: null);
                    notification.RedactContent();
                }
                else
                {
                    notification.ApplyProviderSnapshot(snapshot.Status, snapshot.ErrorCode, snapshot.DateSent, snapshot.Body);
                    if (string.IsNullOrEmpty(snapshot.Body))
                    {
                        notification.RedactContent();
                    }
                }

                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception)
            {
                _logger.LogWarning(
                    "Could not refresh provider status for notification {NotificationId}.",
                    notification.Id);
            }
        }
    }

    private async Task<IReadOnlyList<ContactNumber>> ResolveActiveDestinationsAsync(
        string buyerId,
        int? originalContactNumberId,
        CancellationToken cancellationToken)
    {
        if (originalContactNumberId is int contactNumberId)
        {
            var original = await _contactNumbers.FirstOrDefaultAsync(
                new ContactNumberByIdForBuyerSpecification(contactNumberId, buyerId), cancellationToken);
            if (original is not null)
            {
                return new[] { original };
            }

            return Array.Empty<ContactNumber>();
        }

        return await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
    }

    private static bool CanResend(string status)
    {
        return TerminalFailureStatuses.Contains(status);
    }
}
