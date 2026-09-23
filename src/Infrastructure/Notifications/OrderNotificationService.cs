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
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

/// <summary>Flow 2 implementation — order lifecycle and the messages that accompany it.</summary>
public class OrderNotificationService : IOrderNotificationService
{
    // "a few days later" — comfortably inside the provider's scheduling window and easy to cancel in a demo.
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly ISmsProviderGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly ILogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orderRepository,
        IRepository<OrderNotification> notificationRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IRepository<ContactNumber> contactNumberRepository,
        ISmsProviderGateway gateway,
        IUriComposer uriComposer,
        ILogger<OrderNotificationService> logger)
    {
        _orderRepository = orderRepository;
        _notificationRepository = notificationRepository;
        _catalogItemRepository = catalogItemRepository;
        _contactNumberRepository = contactNumberRepository;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> items,
        Address shipToAddress, CancellationToken ct)
    {
        if (items is null || items.Count == 0)
        {
            return new PlaceOrderResult(PlaceOrderOutcome.EmptyOrder, null, "No items were supplied.");
        }

        if (items.Any(i => i.Quantity <= 0))
        {
            return new PlaceOrderResult(PlaceOrderOutcome.InvalidItems, null, "Quantities must be positive.");
        }

        var ids = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItemRepository.ListAsync(new CatalogItemsSpecification(ids), ct);
        var byId = catalogItems.ToDictionary(c => c.Id);
        if (ids.Any(id => !byId.ContainsKey(id)))
        {
            return new PlaceOrderResult(PlaceOrderOutcome.InvalidItems, null, "One or more catalog items do not exist.");
        }

        var orderItems = items.Select(line =>
        {
            var catalogItem = byId[line.CatalogItemId];
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);
        await _orderRepository.AddAsync(order, ct);
        _logger.LogInformation("Placed order {OrderId} for a shopper.", order.Id);

        // Messaging failure must never fail order placement.
        await NotifyOwnerAsync(order, NotificationKind.OrderPlaced,
            $"eShop: your order #{order.Id} has been placed. Thank you for shopping with us!", ct);

        return new PlaceOrderResult(PlaceOrderOutcome.Placed, order.Id, null);
    }

    public async Task<OrderActionOutcome> DispatchAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null)
        {
            return OrderActionOutcome.NotFound;
        }

        // Gate every side effect on an actual transition — a repeated dispatch does nothing.
        if (!order.MarkDispatched())
        {
            return OrderActionOutcome.NoOp;
        }

        await _orderRepository.UpdateAsync(order, ct);
        _logger.LogInformation("Order {OrderId} marked dispatched.", order.Id);

        await NotifyOwnerAsync(order, NotificationKind.OrderDispatched,
            $"eShop: good news — your order #{order.Id} is on its way!", ct);

        // Queue the delivery follow-up WITH THE PROVIDER for a few days later (no in-app timer).
        await ScheduleFollowUpAsync(order, ct);

        return OrderActionOutcome.Done;
    }

    public async Task<OrderActionOutcome> CancelAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null)
        {
            return OrderActionOutcome.NotFound;
        }

        if (!order.MarkCancelled())
        {
            return OrderActionOutcome.NoOp;
        }

        await _orderRepository.UpdateAsync(order, ct);
        _logger.LogInformation("Order {OrderId} marked cancelled.", order.Id);

        // Call off any not-yet-sent follow-up so it can never reach the customer.
        await CancelPendingFollowUpsAsync(order.Id, ct);

        await NotifyOwnerAsync(order, NotificationKind.OrderCancelled,
            $"eShop: your order #{order.Id} has been cancelled. If this is unexpected, please contact support.", ct);

        return OrderActionOutcome.Done;
    }

    public async Task<IReadOnlyList<OrderNotification>?> GetOrderNotificationsAsync(string ownerId, int orderId,
        bool refreshFromProvider, CancellationToken ct)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null || !string.Equals(order.BuyerId, ownerId, StringComparison.Ordinal))
        {
            return null; // not the caller's order (or does not exist)
        }

        var notifications = await _notificationRepository.ListAsync(
            new OrderNotificationsByOrderSpecification(orderId), ct);

        if (refreshFromProvider)
        {
            foreach (var notification in notifications.Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid)))
            {
                await RefreshNotificationStatusAsync(notification, ct);
            }
        }

        return notifications;
    }

    public async Task<IReadOnlyList<MyOrderView>> GetMyOrdersAsync(string ownerId, CancellationToken ct)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(ownerId), ct);
        var notifications = await _notificationRepository.ListAsync(
            new OrderNotificationsByOwnerSpecification(ownerId), ct);
        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => (IReadOnlyList<OrderNotification>)g.ToList());

        return orders
            .Select(o => new MyOrderView(o,
                byOrder.TryGetValue(o.Id, out var list) ? list : Array.Empty<OrderNotification>()))
            .ToList();
    }

    // --- helpers ---

    private async Task NotifyOwnerAsync(Order order, NotificationKind kind, string body, CancellationToken ct)
    {
        var numbers = await _contactNumberRepository.ListAsync(
            new ContactNumbersByOwnerSpecification(order.BuyerId), ct);
        if (numbers.Count == 0)
        {
            _logger.LogInformation("Order {OrderId}: shopper has no contact number on file; not messaged.", order.Id);
            return;
        }

        foreach (var number in numbers)
        {
            // WRITE ORDER: persist the local record BEFORE the provider call.
            var notification = new OrderNotification(order.Id, order.BuyerId, kind, number.E164Number, body);
            await _notificationRepository.AddAsync(notification, ct);

            try
            {
                var result = await _gateway.SendAsync(number.E164Number, body, ct);
                notification.RecordSent(result.ProviderMessageSid,
                    NotificationStatusMapper.FromProviderStatus(result.ProviderStatusRaw),
                    result.ProviderStatusRaw, result.ErrorCode, result.ErrorMessage, result.ProviderSentAt);
            }
            catch (Exception ex)
            {
                // Never let a messaging failure fail the order operation.
                _logger.LogWarning(ex, "Order {OrderId}: {Kind} message could not be sent.", order.Id, kind);
                notification.RecordSendUnknown();
            }

            await _notificationRepository.UpdateAsync(notification, ct);
        }
    }

    private async Task ScheduleFollowUpAsync(Order order, CancellationToken ct)
    {
        var numbers = await _contactNumberRepository.ListAsync(
            new ContactNumbersByOwnerSpecification(order.BuyerId), ct);
        if (numbers.Count == 0)
        {
            return;
        }

        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        var body = $"eShop: how did the delivery of your order #{order.Id} go? We'd love your feedback.";

        foreach (var number in numbers)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, NotificationKind.DeliveryFollowUp,
                number.E164Number, body, isScheduled: true);
            await _notificationRepository.AddAsync(notification, ct);

            try
            {
                var result = await _gateway.ScheduleAsync(number.E164Number, body, sendAt, ct);
                notification.RecordSent(result.ProviderMessageSid,
                    NotificationStatusMapper.FromProviderStatus(result.ProviderStatusRaw),
                    result.ProviderStatusRaw, result.ErrorCode, result.ErrorMessage, result.ProviderSentAt);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Order {OrderId}: delivery follow-up could not be scheduled.", order.Id);
                notification.RecordSendUnknown();
            }

            await _notificationRepository.UpdateAsync(notification, ct);
        }
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken ct)
    {
        var notifications = await _notificationRepository.ListAsync(
            new OrderNotificationsByOrderSpecification(orderId), ct);

        foreach (var notification in notifications.Where(IsCancelableFollowUp))
        {
            try
            {
                await _gateway.CancelScheduledAsync(notification.ProviderMessageSid!, ct);
                notification.MarkScheduledCancelled();
                await _notificationRepository.UpdateAsync(notification, ct);
                _logger.LogInformation("Order {OrderId}: called off follow-up notification {NotificationId}.",
                    orderId, notification.Id);
            }
            catch (Exception ex)
            {
                // Cancellation is best-effort against the provider; the order cancel still succeeds. The
                // follow-up stays visible as still-scheduled so it can be retried/reconciled.
                _logger.LogWarning(ex, "Order {OrderId}: could not call off follow-up notification {NotificationId}.",
                    orderId, notification.Id);
            }
        }
    }

    private static bool IsCancelableFollowUp(OrderNotification n) =>
        n.Kind == NotificationKind.DeliveryFollowUp
        && n.IsScheduled
        && !string.IsNullOrEmpty(n.ProviderMessageSid)
        && n.DeliveryStatus is NotificationDeliveryStatus.Scheduled
            or NotificationDeliveryStatus.Pending
            or NotificationDeliveryStatus.Unknown;

    private async Task RefreshNotificationStatusAsync(OrderNotification notification, CancellationToken ct)
    {
        try
        {
            var status = await _gateway.FetchStatusAsync(notification.ProviderMessageSid!, ct);
            notification.RefreshStatus(NotificationStatusMapper.FromProviderStatus(status.ProviderStatusRaw),
                status.ProviderStatusRaw, status.ErrorCode, status.ErrorMessage, status.ProviderSentAt);
            await _notificationRepository.UpdateAsync(notification, ct);
        }
        catch (SmsProviderException ex)
        {
            // Keep the last-known status if the provider cannot be reached for a refresh.
            _logger.LogWarning(ex, "Could not refresh status for notification {NotificationId}.", notification.Id);
        }
    }
}
