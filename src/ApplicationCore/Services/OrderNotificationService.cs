using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    // How far ahead the "how did the delivery go?" follow-up is queued. Well within the provider's
    // scheduling window, and far enough out that a cancellation can always beat it.
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly ISmsGateway _smsGateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        ISmsGateway smsGateway,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationService> logger)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _orders = orders;
        _catalogItems = catalogItems;
        _smsGateway = smsGateway;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    // ----------------------------------------------------------------- Flow 1

    public async Task<ContactNumber> RegisterContactNumberAsync(string buyerId, string rawPhoneNumber, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(rawPhoneNumber, nameof(rawPhoneNumber));

        var validation = await _smsGateway.ValidatePhoneNumberAsync(rawPhoneNumber, ct);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalNumber))
        {
            // Reject here, not when a later message fails to go out. Message is phone-free.
            throw new InvalidPhoneNumberException("The number provided is not a usable SMS destination and was not registered.");
        }

        var canonical = validation.CanonicalNumber;

        // Don't store the same canonical number twice for one shopper.
        var existing = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
        var already = existing.FirstOrDefault(c => c.PhoneNumber == canonical);
        if (already is not null)
        {
            return already;
        }

        var contactNumber = new ContactNumber(buyerId, canonical);
        return await _contactNumbers.AddAsync(contactNumber, ct);
    }

    public async Task<IReadOnlyList<ContactNumber>> GetContactNumbersAsync(string buyerId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
    }

    public async Task<bool> RemoveContactNumberAsync(string buyerId, int contactNumberId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        // Scoped by buyer: one shopper can never delete another's number.
        var contactNumber = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByIdSpecification(contactNumberId, buyerId), ct);
        if (contactNumber is null)
        {
            return false;
        }

        await _contactNumbers.DeleteAsync(contactNumber, ct);
        return true;
    }

    // ----------------------------------------------------------------- Flow 2

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(lines, nameof(lines));
        if (lines.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one line.", nameof(lines));
        }

        var catalogItemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(catalogItemIds), ct);

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new ArgumentException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.", nameof(lines));
            }

            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new ArgumentException($"Catalog item {line.CatalogItemId} does not exist.", nameof(lines));

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        // Reuse the app's own order/order-item model. No shipping address is collected on this
        // API, so a neutral placeholder is used — the notification flow is what this endpoint drives.
        var shipToAddress = new Address("Not specified", "Not specified", string.Empty, "Not specified", "00000");
        var order = new Order(buyerId, shipToAddress, items);
        order = await _orders.AddAsync(order, ct);

        var body = $"eShop: thanks! Your order #{order.Id} has been placed. Total {order.Total().ToString("C", CultureInfo.InvariantCulture)}.";
        await NotifyBuyerAsync(order.Id, buyerId, NotificationKind.OrderPlaced, body, ct);

        return order;
    }

    public async Task<bool> DispatchOrderAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdAsync(orderId, ct);
        if (order is null)
        {
            return false;
        }

        var dispatchBody = $"eShop: good news — your order #{order.Id} is on its way!";
        await NotifyBuyerAsync(order.Id, order.BuyerId, NotificationKind.OrderDispatched, dispatchBody, ct);

        // Queue the delivery follow-up WITH THE PROVIDER for a few days out — not held here on a timer.
        var followUpBody = $"eShop: how did the delivery of order #{order.Id} go? We'd love your feedback.";
        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        await ScheduleBuyerFollowUpAsync(order.Id, order.BuyerId, followUpBody, sendAt, ct);

        return true;
    }

    public async Task<bool> CancelOrderAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdAsync(orderId, ct);
        if (order is null)
        {
            return false;
        }

        // Call off any follow-up not yet sent FIRST: asking a customer how their delivery went for a
        // cancelled order is exactly the incident this prevents.
        var scheduled = await _notifications.ListAsync(new ScheduledFollowUpsForOrderSpecification(order.Id), ct);
        foreach (var followUp in scheduled)
        {
            try
            {
                await _smsGateway.CancelScheduledAsync(followUp.MessageSid!, ct);
                followUp.MarkScheduledCanceled();
                await _notifications.UpdateAsync(followUp, ct);
            }
            catch (SmsGatewayException ex)
            {
                // Surface: a follow-up we could not cancel is a real risk, so we do not swallow it silently.
                _logger.LogWarning("Failed to cancel scheduled follow-up notification {0} for order {1}: provider status {2}.",
                    followUp.Id, order.Id, ex.StatusCode?.ToString() ?? "n/a");
                throw;
            }
        }

        var body = $"eShop: your order #{order.Id} has been cancelled. If this is unexpected, please contact support.";
        await NotifyBuyerAsync(order.Id, order.BuyerId, NotificationKind.OrderCancelled, body, ct);

        return true;
    }

    public async Task<IReadOnlyList<OrderWithNotifications>> GetOrdersForBuyerAsync(string buyerId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        if (orders.Count == 0)
        {
            return Array.Empty<OrderWithNotifications>();
        }

        var orderIds = orders.Select(o => o.Id).ToArray();
        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrdersSpecification(orderIds), ct);

        await RefreshStatusesAsync(notifications, ct);

        var byOrder = notifications
            .GroupBy(n => n.OrderId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<OrderNotification>)g.OrderByDescending(n => n.CreatedAt).ToList());

        return orders
            .Select(o => new OrderWithNotifications(
                o,
                byOrder.TryGetValue(o.Id, out var list) ? list : Array.Empty<OrderNotification>()))
            .ToList();
    }

    public async Task<IReadOnlyList<OrderNotification>?> GetNotificationsForOrderAsync(int orderId, string buyerId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var order = await _orders.GetByIdAsync(orderId, ct);
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            // Not the caller's order (or unknown): the caller must never see another's.
            return null;
        }

        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(orderId), ct);
        await RefreshStatusesAsync(notifications, ct);
        return notifications;
    }

    // ----------------------------------------------------------------- Flow 3

    public async Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        // Idempotency: a repeat under the same key returns the earlier result and sends nothing.
        var priorForKey = await _notifications.FirstOrDefaultAsync(
            new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), ct);
        if (priorForKey is not null)
        {
            return priorForKey;
        }

        var original = await _notifications.GetByIdAsync(notificationId, ct);
        if (original is null)
        {
            return null;
        }

        if (string.IsNullOrEmpty(original.Body))
        {
            // Its content was disposed of; there is nothing to resend.
            throw new SmsGatewayException("The message content has been disposed of and cannot be resent.", statusCode: 409);
        }

        var resend = new OrderNotification(original.OrderId, original.BuyerId, NotificationKind.Resend, original.ToNumber, original.Body);
        resend.SetIdempotency(idempotencyKey, original.Id);
        resend = await _notifications.AddAsync(resend, ct);

        try
        {
            var result = await _smsGateway.SendAsync(resend.ToNumber, resend.Body!, ct);
            resend.MarkSent(result.MessageSid, result.Status);
        }
        catch (SmsGatewayException ex)
        {
            _logger.LogWarning("Resend of notification {0} (new {1}) failed: provider status {2}.",
                original.Id, resend.Id, ex.StatusCode?.ToString() ?? "n/a");
            resend.MarkSendFailed(ex.StatusCode?.ToString());
        }

        await _notifications.UpdateAsync(resend, ct);
        return resend;
    }

    public async Task<bool> RedactNotificationContentAsync(int notificationId, CancellationToken ct = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, ct);
        if (notification is null)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(notification.MessageSid))
        {
            // Dispose of the text at the provider too — not merely hide it here. Metadata survives.
            await _smsGateway.RedactContentAsync(notification.MessageSid, ct);
        }

        notification.MarkContentRedacted();
        await _notifications.UpdateAsync(notification, ct);
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default)
    {
        if (toUtc < fromUtc)
        {
            throw new ArgumentException("'to' must not be earlier than 'from'.", nameof(toUtc));
        }

        // Provider's own record, server-side filtered to this application's configured sending number.
        var providerMessages = await _smsGateway.ListOwnMessagesAsync(fromUtc, toUtc, ct);
        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid)
            .ToDictionary(g => g.Key, g => g.First());

        var truncated = providerMessages.Count >= SmsReconciliationLimits.MaxProviderMessages;

        var eshopNotifications = await _notifications.ListAsync(new OrderNotificationsSentInRangeSpecification(fromUtc, toUtc), ct);
        var eshopBySid = eshopNotifications
            .Where(n => !string.IsNullOrEmpty(n.MessageSid))
            .GroupBy(n => n.MessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationEntry>();
        var onlyInProvider = new List<ReconciliationEntry>();
        var onlyInEShop = new List<ReconciliationEntry>();

        foreach (var (sid, message) in providerBySid)
        {
            if (eshopBySid.TryGetValue(sid, out var notification))
            {
                matched.Add(new ReconciliationEntry(sid, true, true, message.Status, notification.ProviderStatus,
                    notification.Id, notification.OrderId, message.DateSentUtc));
            }
            else
            {
                onlyInProvider.Add(new ReconciliationEntry(sid, true, false, message.Status, null, null, null, message.DateSentUtc));
            }
        }

        foreach (var (sid, notification) in eshopBySid)
        {
            if (!providerBySid.ContainsKey(sid))
            {
                onlyInEShop.Add(new ReconciliationEntry(sid, false, true, null, notification.ProviderStatus,
                    notification.Id, notification.OrderId, null));
            }
        }

        return new ReconciliationReport(
            fromUtc, toUtc,
            providerBySid.Count, eshopBySid.Count, matched.Count,
            onlyInProvider, onlyInEShop, matched, truncated);
    }

    // ----------------------------------------------------------------- helpers

    private async Task NotifyBuyerAsync(int orderId, string buyerId, NotificationKind kind, string body, CancellationToken ct)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
        foreach (var number in numbers)
        {
            var notification = new OrderNotification(orderId, buyerId, kind, number.PhoneNumber, body);
            notification = await _notifications.AddAsync(notification, ct);
            try
            {
                var result = await _smsGateway.SendAsync(number.PhoneNumber, body, ct);
                notification.MarkSent(result.MessageSid, result.Status);
            }
            catch (SmsGatewayException ex)
            {
                // A message that cannot be sent must never fail the order operation.
                _logger.LogWarning("Order {0}: {1} notification {2} could not be sent (provider status {3}).",
                    orderId, kind, notification.Id, ex.StatusCode?.ToString() ?? "n/a");
                notification.MarkSendFailed(ex.StatusCode?.ToString());
            }
            await _notifications.UpdateAsync(notification, ct);
        }
    }

    private async Task ScheduleBuyerFollowUpAsync(int orderId, string buyerId, string body, DateTimeOffset sendAt, CancellationToken ct)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
        foreach (var number in numbers)
        {
            var notification = new OrderNotification(orderId, buyerId, NotificationKind.DeliveryFollowUp, number.PhoneNumber, body);
            notification = await _notifications.AddAsync(notification, ct);
            try
            {
                var result = await _smsGateway.ScheduleAsync(number.PhoneNumber, body, sendAt, ct);
                notification.MarkScheduled(result.MessageSid, result.Status, sendAt);
            }
            catch (SmsGatewayException ex)
            {
                _logger.LogWarning("Order {0}: follow-up notification {1} could not be scheduled (provider status {2}).",
                    orderId, notification.Id, ex.StatusCode?.ToString() ?? "n/a");
                notification.MarkSendFailed(ex.StatusCode?.ToString());
            }
            await _notifications.UpdateAsync(notification, ct);
        }
    }

    private async Task RefreshStatusesAsync(IEnumerable<OrderNotification> notifications, CancellationToken ct)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.MessageSid) || IsTerminal(notification.ProviderStatus))
            {
                continue;
            }

            try
            {
                var status = await _smsGateway.FetchStatusAsync(notification.MessageSid, ct);
                notification.UpdateStatus(status.Status, status.ErrorCode);
                await _notifications.UpdateAsync(notification, ct);
            }
            catch (SmsGatewayException ex)
            {
                // A read failure must not break the caller's request.
                _logger.LogWarning("Could not refresh status for notification {0}: provider status {1}.",
                    notification.Id, ex.StatusCode?.ToString() ?? "n/a");
            }
        }
    }

    private static bool IsTerminal(string? status) =>
        status is "delivered" or "undelivered" or "failed" or "canceled" or "not_sent";
}

/// <summary>Guardrails for a reconciliation run so a huge provider account cannot spin an unbounded fetch.</summary>
internal static class SmsReconciliationLimits
{
    public const int MaxProviderMessages = 100_000;
}
