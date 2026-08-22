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
    private readonly IRepository<ShopperContactNumber> _contactNumbers;
    private readonly ITwilioMessagingClient _messaging;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notifications,
        IRepository<ShopperContactNumber> contactNumbers,
        ITwilioMessagingClient messaging,
        IAppLogger<OrderNotificationService> logger)
    {
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _messaging = messaging;
        _logger = logger;
    }

    public Task<IReadOnlyList<OrderNotification>> NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default) =>
        NotifyAsync(order, NotificationKind.OrderPlaced, $"eShopOnWeb: Your order #{order.Id} has been placed. Thank you!", cancellationToken: cancellationToken);

    public async Task<IReadOnlyList<OrderNotification>> NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var dispatched = await NotifyAsync(
            order,
            NotificationKind.OrderDispatched,
            $"eShopOnWeb: Your order #{order.Id} is on its way.",
            cancellationToken: cancellationToken);

        var followUp = await NotifyAsync(
            order,
            NotificationKind.DeliveryFollowUp,
            $"eShopOnWeb: How did the delivery of order #{order.Id} go? We would love your feedback.",
            sendAt: DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay),
            cancellationToken: cancellationToken);

        return dispatched.Concat(followUp).ToList();
    }

    public async Task CancelOutstandingFollowUpsAsync(Order order, CancellationToken cancellationToken = default)
    {
        var followUps = await _notifications.ListAsync(
            new ScheduledFollowUpsByOrderSpecification(order.Id),
            cancellationToken);

        await RefreshFromProviderAsync(followUps, cancellationToken);

        foreach (var followUp in followUps.Where(f => f.IsScheduledOutstanding() && !string.IsNullOrEmpty(f.ProviderMessageSid)))
        {
            try
            {
                var updated = await _messaging.UpdateMessageAsync(
                    followUp.ProviderMessageSid!,
                    new UpdateProviderMessageRequest(null, "canceled"),
                    cancellationToken);
                followUp.ApplyProviderState(updated.Status, updated.Body, updated.ErrorCode, updated.ErrorMessage, updated.DateSent);
                await _notifications.UpdateAsync(followUp, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Failed to cancel a scheduled follow-up for order {OrderId}: {Reason}",
                    order.Id,
                    Redact(ex.Message));
            }
        }
    }

    public Task<IReadOnlyList<OrderNotification>> NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default) =>
        NotifyAsync(order, NotificationKind.OrderCancelled, $"eShopOnWeb: Your order #{order.Id} has been cancelled.", cancellationToken: cancellationToken);

    public async Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notifications.ListAsync(new NotificationsByOrderIdSpecification(orderId), cancellationToken);
        await RefreshFromProviderAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrdersAsync(IReadOnlyCollection<int> orderIds, CancellationToken cancellationToken = default)
    {
        if (orderIds.Count == 0)
        {
            return Array.Empty<OrderNotification>();
        }

        var notifications = await _notifications.ListAsync(new NotificationsByOrderIdsSpecification(orderIds), cancellationToken);
        await RefreshFromProviderAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task RefreshFromProviderAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken = default)
    {
        foreach (var notification in notifications.Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid)))
        {
            try
            {
                var latest = await _messaging.GetMessageAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.ApplyProviderState(latest.Status, latest.Body, latest.ErrorCode, latest.ErrorMessage, latest.DateSent);
                if (!notification.ContentDisposed)
                {
                    await _notifications.UpdateAsync(notification, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Failed to refresh provider status for notification {NotificationId}: {Reason}",
                    notification.Id,
                    Redact(ex.Message));
            }
        }
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new OrderFlowException("An idempotency key is required.");
        }

        var existingForKey = await _notifications.FirstOrDefaultAsync(
            new NotificationByResendIdempotencySpecification(notificationId, idempotencyKey.Trim()),
            cancellationToken);
        if (existingForKey is not null)
        {
            return existingForKey;
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new NotificationNotFoundException(notificationId);

        if (!string.IsNullOrEmpty(original.ProviderMessageSid))
        {
            await RefreshFromProviderAsync(new[] { original }, cancellationToken);
        }

        if (original.ContentDisposed)
        {
            throw new OrderFlowException("The original message content has been disposed and cannot be re-sent.");
        }

        if (!original.DidNotReachShopper())
        {
            throw new OrderFlowException("Only messages that did not reach the shopper can be re-sent.");
        }

        var stillRegistered = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndCanonicalSpecification(original.BuyerId, original.DestinationNumber),
            cancellationToken);
        if (stillRegistered is null)
        {
            throw new OrderFlowException("The destination is no longer registered; the message will not be sent.");
        }

        var body = original.RetrievableBody();
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new OrderFlowException("The original message has no content to re-send.");
        }

        return await SendAndStoreAsync(
            original.OrderId,
            original.BuyerId,
            original.Kind,
            stillRegistered.CanonicalNumber,
            body,
            sendAt: null,
            resendOfNotificationId: original.Id,
            idempotencyKey: idempotencyKey.Trim(),
            cancellationToken);
    }

    public async Task<OrderNotification> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.FirstOrDefaultAsync(
            new NotificationByIdSpecification(notificationId),
            cancellationToken)
            ?? throw new NotificationNotFoundException(notificationId);

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            try
            {
                var updated = await _messaging.UpdateMessageAsync(
                    notification.ProviderMessageSid,
                    new UpdateProviderMessageRequest(string.Empty, null),
                    cancellationToken);

                var verified = updated;
                if (!string.IsNullOrWhiteSpace(verified.Body))
                {
                    verified = await _messaging.GetMessageAsync(notification.ProviderMessageSid, cancellationToken);
                }

                if (!string.IsNullOrWhiteSpace(verified.Body))
                {
                    throw new OrderFlowException("The provider still returns the message text after disposal.");
                }

                notification.ApplyProviderState(verified.Status, verified.Body, verified.ErrorCode, verified.ErrorMessage, verified.DateSent);
            }
            catch (OrderFlowException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Failed to dispose provider content for notification {NotificationId}: {Reason}",
                    notification.Id,
                    Redact(ex.Message));
                throw new OrderFlowException("The provider could not dispose of the message content.");
            }
        }

        notification.MarkContentDisposed();
        await _notifications.UpdateAsync(notification, cancellationToken);
        return notification;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new OrderFlowException("'to' must be on or after 'from'.");
        }

        var fromNumber = _messaging.ConfiguredFromNumber;
        var providerMessages = await _messaging.ListMessagesFromNumberAsync(fromNumber, from, to, cancellationToken);
        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var localInRange = await _notifications.ListAsync(new NotificationsCreatedBetweenSpecification(from, to), cancellationToken);
        var localByProviderSid = (await _notifications.ListAsync(
                new NotificationsByProviderSidsSpecification(providerBySid.Keys.ToList()),
                cancellationToken))
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var local in localInRange.Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid) && !localByProviderSid.ContainsKey(n.ProviderMessageSid!)))
        {
            localByProviderSid[local.ProviderMessageSid!] = local;
        }

        var matched = new List<ReconciliationRow>();
        var providerOnly = new List<ReconciliationRow>();
        var applicationOnly = new List<ReconciliationRow>();

        foreach (var provider in providerBySid.Values)
        {
            if (localByProviderSid.TryGetValue(provider.Sid, out var local))
            {
                matched.Add(ToRow(local, provider, "matched"));
            }
            else
            {
                providerOnly.Add(new ReconciliationRow(
                    null,
                    provider.Sid,
                    "providerOnly",
                    null,
                    provider.Status,
                    provider.DateSent,
                    null));
            }
        }

        var matchedSids = new HashSet<string>(matched.Select(m => m.ProviderMessageSid!), StringComparer.Ordinal);
        foreach (var local in localInRange)
        {
            if (!string.IsNullOrEmpty(local.ProviderMessageSid) && matchedSids.Contains(local.ProviderMessageSid))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(local.ProviderMessageSid) && providerBySid.ContainsKey(local.ProviderMessageSid))
            {
                continue;
            }

            applicationOnly.Add(ToRow(local, null, "applicationOnly"));
        }

        return new ReconciliationReport(from, to, fromNumber, matched, providerOnly, applicationOnly);
    }

    private async Task<IReadOnlyList<OrderNotification>> NotifyAsync(
        Order order,
        NotificationKind kind,
        string body,
        DateTimeOffset? sendAt = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var destinations = await _contactNumbers.ListAsync(
                new ContactNumbersByBuyerSpecification(order.BuyerId),
                cancellationToken);

            if (destinations.Count == 0)
            {
                return Array.Empty<OrderNotification>();
            }

            var created = new List<OrderNotification>();
            foreach (var destination in destinations)
            {
                var notification = await SendAndStoreAsync(
                    order.Id,
                    order.BuyerId,
                    kind,
                    destination.CanonicalNumber,
                    body,
                    sendAt,
                    resendOfNotificationId: null,
                    idempotencyKey: null,
                    cancellationToken);
                created.Add(notification);
            }

            return created;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Notification {Kind} for order {OrderId} could not be completed: {Reason}",
                kind,
                order.Id,
                Redact(ex.Message));
            return Array.Empty<OrderNotification>();
        }
    }

    private async Task<OrderNotification> SendAndStoreAsync(
        int orderId,
        string buyerId,
        NotificationKind kind,
        string destinationNumber,
        string body,
        DateTimeOffset? sendAt,
        int? resendOfNotificationId,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        string? sid = null;
        string? status = null;
        int? errorCode = null;
        string? errorMessage = null;
        DateTimeOffset? dateSent = null;
        string? sendFailureReason = null;

        try
        {
            var sent = await _messaging.SendAsync(new SendProviderMessageRequest(destinationNumber, body, sendAt), cancellationToken);
            sid = sent.Sid;
            status = sent.Status;
            errorCode = sent.ErrorCode;
            errorMessage = sent.ErrorMessage;
            dateSent = sent.DateSent;
        }
        catch (Exception ex)
        {
            sendFailureReason = Redact(ex.Message);
            status = "failed";
            _logger.LogWarning(
                "Provider send failed for order {OrderId} kind {Kind}: {Reason}",
                orderId,
                kind,
                sendFailureReason);
        }

        var notification = new OrderNotification(
            orderId,
            buyerId,
            kind,
            destinationNumber,
            body,
            sid,
            status,
            errorCode,
            errorMessage,
            dateSent,
            sendAt,
            sendFailureReason,
            resendOfNotificationId,
            idempotencyKey);

        return await _notifications.AddAsync(notification, cancellationToken);
    }

    private static ReconciliationRow ToRow(OrderNotification local, ProviderMessage? provider, string match) =>
        new(
            local.Id.ToString(),
            local.ProviderMessageSid ?? provider?.Sid,
            match,
            local.ProviderStatus ?? local.SendFailureReason,
            provider?.Status,
            provider?.DateSent ?? local.ProviderDateSent,
            local.Kind);

    private static string Redact(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return System.Text.RegularExpressions.Regex.Replace(value, @"\+?\d[\d\s().\-]{6,}\d", "[redacted]");
    }
}
