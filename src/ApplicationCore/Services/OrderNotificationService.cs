using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Places orders and keeps shoppers informed by SMS as those orders move. All provider work is
/// best-effort: a message that cannot be sent is recorded as such and never fails the order
/// operation that triggered it. A shopper with no number on file is simply not messaged.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    /// <summary>How long after dispatch the "how did delivery go?" follow-up is scheduled for.</summary>
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly ITwilioMessagingClient _messagingClient;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        ITwilioMessagingClient messagingClient,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _messagingClient = messagingClient;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, CancellationToken cancellationToken = default)
    {
        if (lines is null || lines.Count == 0)
        {
            return PlaceOrderResult.Fail("An order must contain at least one item.");
        }

        if (lines.Any(l => l.Quantity <= 0))
        {
            return PlaceOrderResult.Fail("Every item quantity must be greater than zero.");
        }

        var catalogItemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(catalogItemIds), cancellationToken);

        var missing = catalogItemIds.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            return PlaceOrderResult.Fail($"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        // Reuse the app's existing order model. The API surface carries only items, so the order
        // is created with a placeholder shipping address it does not otherwise use.
        var shipToAddress = new Address("N/A", "N/A", "N/A", "N/A", "00000");
        var order = new Order(buyerId, shipToAddress, orderItems);
        order = await _orders.AddAsync(order, cancellationToken);

        var body = ComposeMessage(NotificationKind.OrderPlaced, order);
        await SendImmediateToAllNumbersAsync(order, NotificationKind.OrderPlaced, body, cancellationToken);

        return PlaceOrderResult.Ok(order.Id);
    }

    public async Task<bool> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            return false;
        }

        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);

        var dispatchBody = ComposeMessage(NotificationKind.OrderDispatched, order);
        var followUpBody = ComposeMessage(NotificationKind.DeliveryFeedback, order);
        var followUpAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);

        foreach (var number in numbers)
        {
            await SendImmediateAsync(order, number.PhoneNumber, NotificationKind.OrderDispatched, dispatchBody, cancellationToken);
            await ScheduleFollowUpAsync(order, number.PhoneNumber, followUpBody, followUpAt, cancellationToken);
        }

        return true;
    }

    public async Task<bool> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            return false;
        }

        // A follow-up that has not yet gone out must never reach the shopper for a cancelled order.
        var pending = await _notifications.ListAsync(new PendingScheduledNotificationsForOrderSpecification(orderId), cancellationToken);
        foreach (var notification in pending)
        {
            try
            {
                await _messagingClient.CancelScheduledAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.MarkCanceled();
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not cancel scheduled follow-up {NotificationId} for cancelled order {OrderId}: {Error}",
                    notification.Id, orderId, ex.Message);
            }
        }

        var body = ComposeMessage(NotificationKind.OrderCancelled, order);
        await SendImmediateToAllNumbersAsync(order, NotificationKind.OrderCancelled, body, cancellationToken);

        return true;
    }

    public async Task<IReadOnlyList<OrderNotificationsView>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var views = new List<OrderNotificationsView>(orders.Count);
        foreach (var order in orders)
        {
            var notifications = await _notifications.ListAsync(new NotificationsByOrderSpecification(order.Id), cancellationToken);
            await RefreshStatusesAsync(notifications, cancellationToken);
            views.Add(new OrderNotificationsView(order, notifications));
        }
        return views;
    }

    public async Task<IReadOnlyList<OrderNotification>?> GetOrderNotificationsForBuyerAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null || order.BuyerId != buyerId)
        {
            return null;
        }

        var notifications = await _notifications.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshStatusesAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return new ResendResult(false, false, 0, "An idempotency key is required.");
        }

        // Repeating a request under the same key must not send a second message.
        var priorForKey = await _notifications.FirstOrDefaultAsync(new NotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (priorForKey is not null)
        {
            return ResendResult.Duplicate(priorForKey.Id);
        }

        var original = await _notifications.FirstOrDefaultAsync(new NotificationByIdSpecification(notificationId), cancellationToken);
        if (original is null)
        {
            return ResendResult.NotFound();
        }

        var body = !string.IsNullOrEmpty(original.Body) ? original.Body! : ComposeGenericResend(original);
        var resend = OrderNotification.CreateResend(original, idempotencyKey);
        try
        {
            var message = await _messagingClient.SendAsync(original.ToNumber, body, cancellationToken);
            resend.RecordProviderResult(message.Sid, message.Status, message.ErrorCode, message.ErrorMessage);
        }
        catch (Exception ex)
        {
            resend.MarkSendFailed(ex.Message);
            _logger.LogWarning("Re-send of notification {NotificationId} failed: {Error}", notificationId, ex.Message);
        }

        resend = await _notifications.AddAsync(resend, cancellationToken);
        return ResendResult.Resent(resend.Id);
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.FirstOrDefaultAsync(new NotificationByIdSpecification(notificationId), cancellationToken);
        if (notification is null)
        {
            return false;
        }

        // Redact the body at the provider so its text is no longer retrievable there either.
        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            await _messagingClient.RedactBodyAsync(notification.ProviderMessageSid!, cancellationToken);
        }

        // The fact a message was sent, and what became of it, survives; only the content is gone.
        notification.MarkContentDisposed();
        await _notifications.UpdateAsync(notification, cancellationToken);
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider for its record of messages sent from our configured number in the range.
        var providerMessages = await _messagingClient.ListSentFromConfiguredNumberAsync(from, to, cancellationToken);
        var providerInRange = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid) && m.DateSent.HasValue && m.DateSent.Value >= from && m.DateSent.Value <= to)
            .GroupBy(m => m.Sid!)
            .ToDictionary(g => g.Key, g => g.First());

        // What eShop believes it sent in the range: notifications with a provider message id.
        var eshopNotifications = await _notifications.ListAsync(new NotificationsCreatedBetweenSpecification(from, to), cancellationToken);
        var eshopBySid = eshopNotifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        var eshopOnly = new List<ReconciliationEntry>();

        foreach (var (sid, message) in providerInRange)
        {
            if (eshopBySid.TryGetValue(sid, out var notification))
            {
                matched.Add(new ReconciliationEntry(sid, message.Status, message.ErrorCode, message.DateSent, notification.Id, notification.Kind));
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry(sid, message.Status, message.ErrorCode, message.DateSent, null, null));
            }
        }

        foreach (var (sid, notification) in eshopBySid)
        {
            if (!providerInRange.ContainsKey(sid))
            {
                eshopOnly.Add(new ReconciliationEntry(sid, notification.Status, notification.ErrorCode, null, notification.Id, notification.Kind));
            }
        }

        return new ReconciliationReport(
            from, to, _messagingClient.ConfiguredFromNumber,
            providerInRange.Count, eshopBySid.Count, matched.Count,
            matched, providerOnly, eshopOnly);
    }

    // ----- helpers -------------------------------------------------------------------------

    private async Task SendImmediateToAllNumbersAsync(Order order, NotificationKind kind, string body, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
        foreach (var number in numbers)
        {
            await SendImmediateAsync(order, number.PhoneNumber, kind, body, cancellationToken);
        }
    }

    private async Task SendImmediateAsync(Order order, string toNumber, NotificationKind kind, string body, CancellationToken cancellationToken)
    {
        var notification = OrderNotification.CreateImmediate(order.Id, order.BuyerId, toNumber, kind, body);
        try
        {
            var message = await _messagingClient.SendAsync(toNumber, body, cancellationToken);
            notification.RecordProviderResult(message.Sid, message.Status, message.ErrorCode, message.ErrorMessage);
        }
        catch (Exception ex)
        {
            notification.MarkSendFailed(ex.Message);
            _logger.LogWarning("Sending {Kind} notification for order {OrderId} failed: {Error}", kind, order.Id, ex.Message);
        }
        await _notifications.AddAsync(notification, cancellationToken);
    }

    private async Task ScheduleFollowUpAsync(Order order, string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken)
    {
        var notification = OrderNotification.CreateScheduled(order.Id, order.BuyerId, toNumber, NotificationKind.DeliveryFeedback, body, sendAt);
        try
        {
            var message = await _messagingClient.ScheduleAsync(toNumber, body, sendAt, cancellationToken);
            var status = string.IsNullOrEmpty(message.Status) ? NotificationStatus.Scheduled : message.Status;
            notification.RecordProviderResult(message.Sid, status, message.ErrorCode, message.ErrorMessage);
        }
        catch (Exception ex)
        {
            notification.MarkSendFailed(ex.Message);
            _logger.LogWarning("Scheduling delivery follow-up for order {OrderId} failed: {Error}", order.Id, ex.Message);
        }
        await _notifications.AddAsync(notification, cancellationToken);
    }

    private async Task RefreshStatusesAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid) || NotificationStatus.IsTerminal(notification.Status))
            {
                continue;
            }

            try
            {
                var message = await _messagingClient.FetchAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.UpdateDeliveryOutcome(message.Status, message.ErrorCode, message.ErrorMessage);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not refresh delivery outcome for notification {NotificationId}: {Error}", notification.Id, ex.Message);
            }
        }
    }

    private string ComposeMessage(NotificationKind kind, Order order) => kind switch
    {
        NotificationKind.OrderPlaced =>
            $"eShop: thanks! Your order #{order.Id} has been placed. Total ${order.Total():0.00}.",
        NotificationKind.OrderDispatched =>
            $"eShop: good news! Your order #{order.Id} is on its way.",
        NotificationKind.DeliveryFeedback =>
            $"eShop: how did the delivery of your order #{order.Id} go? We'd love your feedback.",
        NotificationKind.OrderCancelled =>
            $"eShop: your order #{order.Id} has been cancelled. If this is unexpected, please contact us.",
        _ => $"eShop: an update about your order #{order.Id}."
    };

    private string ComposeGenericResend(OrderNotification original) =>
        $"eShop: an update about your order #{original.OrderId}.";
}
