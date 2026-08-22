using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
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
    private readonly IRepository<Order> _orders;
    private readonly ITwilioMessagingClient _messaging;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notifications,
        IRepository<ContactNumber> contactNumbers,
        IRepository<Order> orders,
        ITwilioMessagingClient messaging,
        IAppLogger<OrderNotificationService> logger)
    {
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _orders = orders;
        _messaging = messaging;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShopOnWeb: Your order #{order.Id} has been placed. Thank you!";
        return TrySendAsync(order, NotificationKind.OrderPlaced, body, sendAt: null, cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShopOnWeb: Order #{order.Id} is on its way.";
        await TrySendAsync(order, NotificationKind.OrderDispatched, body, sendAt: null, cancellationToken);

        var followUp = $"eShopOnWeb: How did delivery of order #{order.Id} go? Reply with your feedback.";
        await TrySendAsync(order, NotificationKind.DeliveryFollowUp, followUp, DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay), cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        await CancelScheduledFollowUpsAsync(order.Id, cancellationToken);
        var body = $"eShopOnWeb: Order #{order.Id} has been cancelled.";
        await TrySendAsync(order, NotificationKind.OrderCancelled, body, sendAt: null, cancellationToken);
    }

    public async Task CancelScheduledForContactAsync(int contactNumberId, CancellationToken cancellationToken = default)
    {
        var pending = await _notifications.ListAsync(new ScheduledNotificationsByContactSpec(contactNumberId), cancellationToken);
        foreach (var notification in pending)
        {
            await CancelAtProviderAsync(notification, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<NotificationView>> ListForOrderAsync(int orderId, string buyerId, bool isAdministrator, CancellationToken cancellationToken = default)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new KeyNotFoundException("Order not found.");
        }

        if (!isAdministrator && !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new KeyNotFoundException("Order not found.");
        }

        var list = await _notifications.ListAsync(new NotificationsByOrderIdSpec(orderId), cancellationToken);
        await RefreshFromProviderAsync(list, cancellationToken);
        return list.Select(ToView).ToList();
    }

    public async Task<IReadOnlyList<NotificationView>> ListForBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var list = await _notifications.ListAsync(new NotificationsByBuyerSpec(buyerId), cancellationToken);
        await RefreshFromProviderAsync(list, cancellationToken);
        return list.Select(ToView).ToList();
    }

    public async Task<int> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
        }

        var existing = await _notifications.FirstOrDefaultAsync(
            new ResendByIdempotencySpec(notificationId, idempotencyKey.Trim()), cancellationToken);
        if (existing is not null)
        {
            return existing.Id;
        }

        var source = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (source is null)
        {
            throw new KeyNotFoundException("Notification not found.");
        }

        await RefreshFromProviderAsync(new[] { source }, cancellationToken);

        if (source.ContentRedacted || string.IsNullOrEmpty(source.Body))
        {
            throw new OrderStateException("The original message content is no longer available to resend.");
        }

        if (!source.DidNotReachShopper())
        {
            throw new OrderStateException("Only messages that did not reach the shopper can be resent.");
        }

        var contact = await ResolveActiveDestinationAsync(source.BuyerId, source.ContactNumberId, source.DestinationNumber, cancellationToken);
        if (contact is null)
        {
            throw new OrderStateException("The destination number is no longer on file, so the message cannot be resent.");
        }

        var resend = new OrderNotification(
            source.OrderId,
            source.BuyerId,
            NotificationKind.Resend,
            contact.PhoneNumber,
            contact.Id,
            source.Body,
            scheduledFor: null,
            resendOfNotificationId: source.Id,
            idempotencyKey: idempotencyKey.Trim());

        await _notifications.AddAsync(resend, cancellationToken);

        var raced = await _notifications.FirstOrDefaultAsync(
            new ResendByIdempotencySpec(notificationId, idempotencyKey.Trim()), cancellationToken);
        if (raced is not null && raced.Id != resend.Id)
        {
            await _notifications.DeleteAsync(resend, cancellationToken);
            return raced.Id;
        }

        await SubmitAsync(resend, sendAt: null, cancellationToken);
        return resend.Id;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            throw new KeyNotFoundException("Notification not found.");
        }

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            try
            {
                var updated = await _messaging.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
                notification.ApplyProviderState(updated.Status, updated.DateSent, updated.ErrorCode, bodyIfPresent: null, contentAlreadyRedacted: true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to redact provider content for notification {NotificationId}: {Error}", notification.Id, Sanitize(ex.Message));
                throw;
            }
        }

        notification.MarkContentRedacted();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("The 'to' timestamp must be on or after 'from'.");
        }

        var fromNumber = _messaging.GetConfiguredFromNumber();
        var providerMessages = await _messaging.ListFromNumberAsync(fromNumber, from, to, cancellationToken);
        var local = await _notifications.ListAsync(new NotificationsInCreatedRangeSpec(from, to), cancellationToken);

        var localBySid = local
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var items = new List<ReconciliationItem>();
        var matchedSids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var provider in providerMessages)
        {
            if (localBySid.TryGetValue(provider.Sid, out var localMatch))
            {
                matchedSids.Add(provider.Sid);
                items.Add(new ReconciliationItem(
                    localMatch.Id.ToString(),
                    provider.Sid,
                    "matched",
                    provider.Status,
                    provider.DateSent,
                    localMatch.CreatedAt,
                    localMatch.Kind));
            }
            else
            {
                items.Add(new ReconciliationItem(
                    null,
                    provider.Sid,
                    "provider_only",
                    provider.Status,
                    provider.DateSent,
                    provider.DateCreated,
                    null));
            }
        }

        foreach (var notification in local)
        {
            if (!string.IsNullOrEmpty(notification.ProviderMessageSid) && matchedSids.Contains(notification.ProviderMessageSid))
            {
                continue;
            }

            items.Add(new ReconciliationItem(
                notification.Id.ToString(),
                notification.ProviderMessageSid,
                "local_only",
                notification.ProviderStatus,
                notification.ProviderDateSent,
                notification.CreatedAt,
                notification.Kind));
        }

        return new ReconciliationReport(
            from,
            to,
            fromNumber,
            items,
            items.Count(i => i.Match == "matched"),
            items.Count(i => i.Match == "provider_only"),
            items.Count(i => i.Match == "local_only"));
    }

    private async Task TrySendAsync(Order order, NotificationKind kind, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        try
        {
            var contact = await GetPrimaryContactAsync(order.BuyerId, cancellationToken);
            if (contact is null)
            {
                _logger.LogInformation("Skipping {Kind} SMS for order {OrderId}; buyer {BuyerId} has no number on file.", kind, order.Id, order.BuyerId);
                return;
            }

            var notification = new OrderNotification(
                order.Id,
                order.BuyerId,
                kind,
                contact.PhoneNumber,
                contact.Id,
                body,
                sendAt);

            await _notifications.AddAsync(notification, cancellationToken);
            await SubmitAsync(notification, sendAt, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("SMS notification {Kind} for order {OrderId} did not block the order operation: {Error}", kind, order.Id, Sanitize(ex.Message));
        }
    }

    private async Task SubmitAsync(OrderNotification notification, DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        try
        {
            var sent = await _messaging.SendAsync(new SendSmsRequest(notification.DestinationNumber, notification.Body ?? string.Empty, sendAt), cancellationToken);
            notification.RecordProviderAcceptance(sent.Sid, sent.Status, sent.DateSent, sent.ErrorCode);
            await _notifications.UpdateAsync(notification, cancellationToken);
            _logger.LogInformation(
                "Submitted {Kind} SMS for order {OrderId} as provider message {MessageSid} with status {Status}.",
                notification.Kind, notification.OrderId, sent.Sid, sent.Status);
        }
        catch (Exception ex)
        {
            notification.RecordSubmitFailure(Sanitize(ex.Message));
            await _notifications.UpdateAsync(notification, cancellationToken);
            _logger.LogWarning(
                "Provider rejected {Kind} SMS for order {OrderId} notification {NotificationId}: {Error}",
                notification.Kind, notification.OrderId, notification.Id, Sanitize(ex.Message));
        }
    }

    private async Task CancelScheduledFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var pending = await _notifications.ListAsync(new ScheduledFollowUpsByOrderSpec(orderId), cancellationToken);
        foreach (var notification in pending)
        {
            await CancelAtProviderAsync(notification, cancellationToken);
        }
    }

    private async Task CancelAtProviderAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            return;
        }

        try
        {
            var updated = await _messaging.CancelAsync(notification.ProviderMessageSid, cancellationToken);
            notification.ApplyProviderState(updated.Status, updated.DateSent, updated.ErrorCode, updated.Body, notification.ContentRedacted);
            await _notifications.UpdateAsync(notification, cancellationToken);
            _logger.LogInformation("Cancelled provider message {MessageSid} for notification {NotificationId}.", updated.Sid, notification.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not cancel provider message for notification {NotificationId}: {Error}", notification.Id, Sanitize(ex.Message));
        }
    }

    private async Task RefreshFromProviderAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid) || notification.IsTerminalProviderStatus())
            {
                continue;
            }

            try
            {
                var current = await _messaging.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                var body = notification.ContentRedacted ? null : current.Body;
                notification.ApplyProviderState(current.Status, current.DateSent, current.ErrorCode, body, notification.ContentRedacted);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not refresh provider status for notification {NotificationId}: {Error}", notification.Id, Sanitize(ex.Message));
            }
        }
    }

    private async Task<ContactNumber?> GetPrimaryContactAsync(string buyerId, CancellationToken cancellationToken)
    {
        var contacts = await _contactNumbers.ListAsync(new ActiveContactNumbersByBuyerSpec(buyerId), cancellationToken);
        return contacts.FirstOrDefault();
    }

    private async Task<ContactNumber?> ResolveActiveDestinationAsync(string buyerId, int? contactNumberId, string destinationNumber, CancellationToken cancellationToken)
    {
        if (contactNumberId.HasValue)
        {
            var byId = await _contactNumbers.GetByIdAsync(contactNumberId.Value, cancellationToken);
            if (byId is not null && !byId.IsDeleted && byId.BuyerId == buyerId)
            {
                return byId;
            }
        }

        var byPhone = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndPhoneSpec(buyerId, destinationNumber), cancellationToken);
        if (byPhone is not null && !byPhone.IsDeleted)
        {
            return byPhone;
        }

        return await GetPrimaryContactAsync(buyerId, cancellationToken);
    }

    private static NotificationView ToView(OrderNotification n) =>
        new(
            n.Id,
            n.OrderId,
            n.Kind,
            n.ProviderMessageSid,
            n.ProviderStatus,
            n.ProviderErrorCode,
            n.ContentRedacted ? null : n.Body,
            n.ContentRedacted,
            n.CreatedAt,
            n.ScheduledFor,
            n.ProviderDateSent,
            n.SubmitError,
            n.ResendOfNotificationId);

    private static string Sanitize(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return message;
        }

        return System.Text.RegularExpressions.Regex.Replace(message, @"\+?\d{8,15}", "[redacted]");
    }
}
