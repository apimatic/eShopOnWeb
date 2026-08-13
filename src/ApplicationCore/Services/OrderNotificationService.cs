using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    // How far ahead the "how did the delivery go?" follow-up is queued with the provider.
    private static readonly TimeSpan FollowUpLeadTime = TimeSpan.FromDays(3);

    // Logical sender recorded for scheduled follow-ups (they go through the Messaging Service, not the
    // FromNumber), so reconciliation — which lines up FromNumber traffic — does not expect them.
    private const string ScheduledSenderMarker = "messaging-service";

    // Delivery outcomes past which there is nothing more to refresh from the provider.
    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "delivered", "undelivered", "failed", "canceled",
        NotificationDeliveryStatus.Canceled, NotificationDeliveryStatus.SendFailed
    };

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly ISmsNotificationGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        ISmsNotificationGateway gateway,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines, CancellationToken ct = default)
    {
        if (lines is null || lines.Count == 0)
        {
            return PlaceOrderResult.Invalid("An order must have at least one item.");
        }
        if (lines.Any(l => l.Quantity <= 0))
        {
            return PlaceOrderResult.Invalid("Every item quantity must be greater than zero.");
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), ct);
        if (catalogItems.Count != ids.Length)
        {
            var found = catalogItems.Select(c => c.Id).ToHashSet();
            var missing = ids.Where(id => !found.Contains(id));
            return PlaceOrderResult.Invalid($"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        // Reuse the app's existing Order/OrderItem model rather than a parallel one.
        var items = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, PlaceholderShippingAddress(), items);
        order = await _orders.AddAsync(order, ct);

        await NotifyImmediateAsync(order.Id, buyerId, NotificationKind.OrderPlaced, BuildBody(NotificationKind.OrderPlaced, order.Id), ct);

        return PlaceOrderResult.Placed(order.Id);
    }

    public async Task<OrderActionResult> DispatchOrderAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdAsync(orderId, ct);
        if (order is null)
        {
            return OrderActionResult.NotFound;
        }

        // Tell the shopper it is on its way.
        await NotifyImmediateAsync(order.Id, order.BuyerId, NotificationKind.OrderDispatched, BuildBody(NotificationKind.OrderDispatched, order.Id), ct);

        // Queue the delivery follow-up WITH the provider for a few days later (not held in this app).
        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpLeadTime);
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), ct);
        foreach (var number in numbers)
        {
            var followUp = new OrderNotification(
                order.Id, order.BuyerId, number.PhoneNumber, ScheduledSenderMarker,
                NotificationKind.DeliveryFollowUp, BuildBody(NotificationKind.DeliveryFollowUp, order.Id),
                isScheduled: true, scheduledFor: sendAt);
            try
            {
                var result = await _gateway.ScheduleAsync(number.PhoneNumber, followUp.Body!, sendAt, ct);
                followUp.MarkAccepted(result.ProviderMessageSid, result.DeliveryStatus);
            }
            catch (SmsGatewayException ex)
            {
                followUp.MarkSendFailed(ex.Message);
                _logger.LogWarning("Failed to schedule delivery follow-up for order {0}: {1}", order.Id, ex.Message);
            }
            await _notifications.AddAsync(followUp, ct);
        }

        var produced = await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(order.Id), ct);
        return OrderActionResult.Completed(produced);
    }

    public async Task<OrderActionResult> CancelOrderAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdAsync(orderId, ct);
        if (order is null)
        {
            return OrderActionResult.NotFound;
        }

        // Call off any follow-up that has NOT yet gone out, BEFORE anything else — asking a customer how
        // their delivery went for a cancelled order is exactly the incident this must prevent.
        var existing = await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(orderId), ct);
        foreach (var followUp in existing.Where(IsCancelableScheduledFollowUp))
        {
            try
            {
                await _gateway.CancelScheduledMessageAsync(followUp.ProviderMessageSid!, ct);
                followUp.MarkScheduleCanceled();
                await _notifications.UpdateAsync(followUp, ct);
            }
            catch (SmsGatewayException ex)
            {
                // Best-effort: if the provider will not cancel it (e.g. it already went out), record and move on.
                _logger.LogWarning("Could not cancel scheduled follow-up {0} for order {1}: {2}", followUp.Id, order.Id, ex.Message);
            }
        }

        // Then tell the shopper the order was cancelled.
        await NotifyImmediateAsync(order.Id, order.BuyerId, NotificationKind.OrderCancelled, BuildBody(NotificationKind.OrderCancelled, order.Id), ct);

        var affected = await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(order.Id), ct);
        return OrderActionResult.Completed(affected);
    }

    public async Task<IReadOnlyList<OrderWithNotifications>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        var notifications = await _notifications.ListAsync(new OrderNotificationsByBuyerSpecification(buyerId), ct);
        await RefreshDeliveryOutcomesAsync(notifications, ct);

        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => (IReadOnlyList<OrderNotification>)g.ToList());
        return orders
            .Select(o => new OrderWithNotifications(o, byOrder.TryGetValue(o.Id, out var ns) ? ns : Array.Empty<OrderNotification>()))
            .ToList();
    }

    public async Task<IReadOnlyList<OrderNotification>?> GetOrderNotificationsForOwnerAsync(int orderId, string buyerId, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdAsync(orderId, ct);
        if (order is null || order.BuyerId != buyerId)
        {
            // Not the caller's order (or does not exist): a shopper never sees another's order.
            return null;
        }

        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(orderId), ct);
        await RefreshDeliveryOutcomesAsync(notifications, ct);
        return notifications;
    }

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return ResendResult.CannotResend("An idempotency key is required.");
        }

        // Repeating the request under the same key must not send a second message.
        var priorResend = await _notifications.FirstOrDefaultAsync(new OrderNotificationByResendKeySpecification(idempotencyKey), ct);
        if (priorResend is not null)
        {
            return ResendResult.Resent(priorResend);
        }

        var original = await _notifications.GetByIdAsync(notificationId, ct);
        if (original is null)
        {
            return ResendResult.NotFound;
        }
        if (string.IsNullOrEmpty(original.Body))
        {
            return ResendResult.CannotResend("The message content is no longer available to resend.");
        }

        var resend = new OrderNotification(
            original.OrderId, original.BuyerId, original.ToPhoneNumber, _gateway.SendingNumber,
            original.Kind, original.Body, isScheduled: false);
        resend.TagAsResend(original.Id, idempotencyKey);
        try
        {
            var result = await _gateway.SendAsync(original.ToPhoneNumber, original.Body, ct);
            resend.MarkAccepted(result.ProviderMessageSid, result.DeliveryStatus);
        }
        catch (SmsGatewayException ex)
        {
            resend.MarkSendFailed(ex.Message);
            _logger.LogWarning("Resend of notification {0} could not be handed to the provider: {1}", original.Id, ex.Message);
        }
        resend = await _notifications.AddAsync(resend, ct);
        return ResendResult.Resent(resend);
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken ct = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, ct);
        if (notification is null)
        {
            return false;
        }

        // Remove the content at the provider first (so it is genuinely gone there); if the provider is
        // unavailable this throws and we do NOT claim success. The record itself survives either way.
        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            await _gateway.RedactContentAsync(notification.ProviderMessageSid, ct);
        }

        notification.DisposeContent();
        await _notifications.UpdateAsync(notification, ct);
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var providerMessages = await _gateway.ListSentMessagesAsync(from, to, ct);
        var eShopRecords = await _notifications.ListAsync(new EShopSentNotificationsInRangeSpecification(_gateway.SendingNumber, from, to), ct);

        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid)
            .ToDictionary(g => g.Key, g => g.First());
        var eShopBySid = eShopRecords
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationMatch>();
        var eShopOnly = new List<ReconciliationEShopOnly>();
        foreach (var (sid, n) in eShopBySid)
        {
            if (providerBySid.TryGetValue(sid, out var pm))
            {
                matched.Add(new ReconciliationMatch(sid, pm.Status, n.Id, n.Kind, n.DeliveryStatus));
            }
            else
            {
                eShopOnly.Add(new ReconciliationEShopOnly(n.Id, sid, n.Kind, n.DeliveryStatus));
            }
        }

        var providerOnly = providerBySid
            .Where(kv => !eShopBySid.ContainsKey(kv.Key))
            .Select(kv => new ReconciliationProviderOnly(kv.Key, kv.Value.Status, kv.Value.To, kv.Value.DateSent))
            .ToList();

        return new ReconciliationReport(from, to, _gateway.SendingNumber, matched, providerOnly, eShopOnly);
    }

    // ----- helpers -----

    private async Task NotifyImmediateAsync(int orderId, string buyerId, NotificationKind kind, string body, CancellationToken ct)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
        // A shopper with no number on file is simply not messaged.
        foreach (var number in numbers)
        {
            var notification = new OrderNotification(orderId, buyerId, number.PhoneNumber, _gateway.SendingNumber, kind, body);
            try
            {
                var result = await _gateway.SendAsync(number.PhoneNumber, body, ct);
                notification.MarkAccepted(result.ProviderMessageSid, result.DeliveryStatus);
            }
            catch (SmsGatewayException ex)
            {
                // A message that cannot be sent must NEVER fail the underlying operation.
                notification.MarkSendFailed(ex.Message);
                _logger.LogWarning("Failed to send {0} notification for order {1}: {2}", kind, orderId, ex.Message);
            }
            await _notifications.AddAsync(notification, ct);
        }
    }

    private async Task RefreshDeliveryOutcomesAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken ct)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid) || TerminalStatuses.Contains(notification.DeliveryStatus))
            {
                continue;
            }
            try
            {
                var state = await _gateway.FetchDeliveryStateAsync(notification.ProviderMessageSid, ct);
                notification.UpdateDeliveryStatus(state.Status, state.ErrorCode, state.ErrorMessage);
                await _notifications.UpdateAsync(notification, ct);
            }
            catch (SmsGatewayException ex)
            {
                // A read failure must not break the listing — degrade to the last known outcome.
                _logger.LogWarning("Could not refresh delivery outcome for notification {0}: {1}", notification.Id, ex.Message);
            }
        }
    }

    private static bool IsCancelableScheduledFollowUp(OrderNotification n) =>
        n.Kind == NotificationKind.DeliveryFollowUp &&
        n.IsScheduled &&
        !string.IsNullOrEmpty(n.ProviderMessageSid) &&
        !TerminalStatuses.Contains(n.DeliveryStatus);

    private static string BuildBody(NotificationKind kind, int orderId) => kind switch
    {
        NotificationKind.OrderPlaced => $"eShopOnWeb: thanks for your order! Order #{orderId} has been placed.",
        NotificationKind.OrderDispatched => $"eShopOnWeb: good news — your order #{orderId} is on its way!",
        NotificationKind.OrderCancelled => $"eShopOnWeb: your order #{orderId} has been cancelled.",
        NotificationKind.DeliveryFollowUp => $"eShopOnWeb: how did the delivery of order #{orderId} go? We'd love your feedback.",
        _ => $"eShopOnWeb: an update about your order #{orderId}."
    };

    // No shipping address is collected by this API; orders reuse the existing model with a placeholder.
    private static Address PlaceholderShippingAddress() => new("N/A", "N/A", "N/A", "N/A", "N/A");
}
