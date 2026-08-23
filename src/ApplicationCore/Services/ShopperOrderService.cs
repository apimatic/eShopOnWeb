using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ShopperOrderService : IShopperOrderService, IOrderFulfillmentService
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromHours(72);

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<BuyerContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IUriComposer _uriComposer;
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<ShopperOrderService> _logger;

    public ShopperOrderService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<BuyerContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        IUriComposer uriComposer,
        ISmsGateway smsGateway,
        IAppLogger<ShopperOrderService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _uriComposer = uriComposer;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<OrderCatalogItem> items,
        Address shipTo,
        CancellationToken cancellationToken)
    {
        if (items == null || items.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one catalog item.");
        }

        var catalogIds = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(catalogIds), cancellationToken);
        var catalogById = catalogItems.ToDictionary(c => c.Id);

        var orderItems = new List<OrderItem>();
        foreach (var item in items)
        {
            if (item.Quantity <= 0)
            {
                throw new ArgumentException("Quantity must be greater than zero.");
            }

            if (!catalogById.TryGetValue(item.CatalogItemId, out var catalogItem))
            {
                throw new ArgumentException($"Catalog item {item.CatalogItemId} was not found.");
            }

            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, item.Quantity));
        }

        var order = new Order(buyerId, shipTo, orderItems);
        await _orders.AddAsync(order, cancellationToken);

        await NotifyAsync(
            order,
            OrderNotificationKind.OrderPlaced,
            $"eShopOnWeb: your order #{order.Id} has been placed.",
            sendAt: null,
            cancellationToken);

        return order;
    }

    public async Task<IReadOnlyList<ShopperOrderSummary>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        if (orders.Count == 0)
        {
            return Array.Empty<ShopperOrderSummary>();
        }

        var notifications = await _notifications.ListAsync(
            new OrderNotificationsByOrderIdsSpecification(orders.Select(o => o.Id).ToArray()),
            cancellationToken);

        await RefreshStatusesAsync(notifications, cancellationToken);

        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => (IReadOnlyList<OrderNotification>)g.ToList());
        return orders
            .Select(order => new ShopperOrderSummary(
                order,
                byOrder.TryGetValue(order.Id, out var list) ? list : Array.Empty<OrderNotification>()))
            .ToList();
    }

    public async Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(
        string buyerId,
        int orderId,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order == null)
        {
            return Array.Empty<OrderNotification>();
        }

        if (!isAdmin && !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            return Array.Empty<OrderNotification>();
        }

        var notifications = await _notifications.ListAsync(
            new OrderNotificationsByOrderIdSpecification(orderId),
            cancellationToken);
        await RefreshStatusesAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken)
            ?? throw new ArgumentException($"Order {orderId} was not found.");

        order.MarkDispatched();
        await _orders.UpdateAsync(order, cancellationToken);

        await NotifyAsync(
            order,
            OrderNotificationKind.OrderDispatched,
            $"eShopOnWeb: your order #{order.Id} is on its way.",
            sendAt: null,
            cancellationToken);

        await NotifyAsync(
            order,
            OrderNotificationKind.DeliveryFollowUp,
            $"eShopOnWeb: how did your delivery go for order #{order.Id}?",
            sendAt: DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay),
            cancellationToken);

        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken)
            ?? throw new ArgumentException($"Order {orderId} was not found.");

        order.MarkCancelled();
        await _orders.UpdateAsync(order, cancellationToken);

        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);

        await NotifyAsync(
            order,
            OrderNotificationKind.OrderCancelled,
            $"eShopOnWeb: your order #{order.Id} has been cancelled.",
            sendAt: null,
            cancellationToken);

        return order;
    }

    private async Task NotifyAsync(
        Order order,
        OrderNotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        var destination = await GetCurrentDestinationAsync(order.BuyerId, cancellationToken);
        if (destination == null)
        {
            _logger.LogInformation("Skipping SMS for order {OrderId} kind {Kind}; buyer has no contact number.", order.Id, kind);
            return;
        }

        var notification = new OrderNotification(order.Id, order.BuyerId, kind, destination, body);
        await _notifications.AddAsync(notification, cancellationToken);

        try
        {
            var result = await _smsGateway.SendAsync(new SmsSendRequest(destination, body, sendAt), cancellationToken);
            notification.AttachProviderResult(
                result.ProviderSid,
                result.OutcomeUnknown ? "unknown" : result.Status,
                result.ErrorCode,
                result.ErrorMessage,
                sendAt);
            if (!result.Accepted && !result.OutcomeUnknown)
            {
                _logger.LogWarning("SMS for order {OrderId} kind {Kind} was not accepted by the provider. Notification {NotificationId}.", order.Id, kind, notification.Id);
            }
        }
        catch (Exception ex)
        {
            notification.MarkSendFailed("The provider did not accept the message.");
            _logger.LogWarning("SMS for order {OrderId} kind {Kind} failed. Notification {NotificationId}. {ExceptionType}", order.Id, kind, notification.Id, ex.GetType().Name);
        }

        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var notifications = await _notifications.ListAsync(
            new OrderNotificationsByOrderIdSpecification(orderId),
            cancellationToken);

        foreach (var followUp in notifications.Where(n => n.Kind == OrderNotificationKind.DeliveryFollowUp && !string.IsNullOrWhiteSpace(n.ProviderSid)))
        {
            var status = followUp.ProviderStatus?.ToLowerInvariant();
            if (status is "sent" or "delivered" or "undelivered" or "failed" or "canceled" or "cancelled")
            {
                continue;
            }

            try
            {
                var snapshot = await _smsGateway.CancelScheduledAsync(followUp.ProviderSid!, cancellationToken);
                if (snapshot != null)
                {
                    followUp.RefreshFromProvider(
                        snapshot.Status ?? "canceled",
                        snapshot.ErrorCode,
                        snapshot.ErrorMessage,
                        snapshot.Body);
                    await _notifications.UpdateAsync(followUp, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Could not cancel follow-up {NotificationId} for order {OrderId}. {ExceptionType}",
                    followUp.Id,
                    orderId,
                    ex.GetType().Name);
            }
        }
    }

    private async Task RefreshStatusesAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrWhiteSpace(notification.ProviderSid))
            {
                continue;
            }

            try
            {
                var snapshot = await _smsGateway.FetchAsync(notification.ProviderSid, cancellationToken);
                if (snapshot == null)
                {
                    continue;
                }

                notification.RefreshFromProvider(
                    snapshot.Status ?? notification.ProviderStatus,
                    snapshot.ErrorCode,
                    snapshot.ErrorMessage,
                    notification.BodyRedacted ? null : snapshot.Body);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Could not refresh notification {NotificationId}. {ExceptionType}",
                    notification.Id,
                    ex.GetType().Name);
            }
        }
    }

    private async Task<string?> GetCurrentDestinationAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new BuyerContactNumbersSpecification(buyerId), cancellationToken);
        return numbers.FirstOrDefault()?.CanonicalNumber;
    }
}
