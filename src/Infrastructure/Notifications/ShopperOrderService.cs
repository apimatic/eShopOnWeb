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

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

public sealed class ShopperOrderService : IShopperOrderService
{
    // No shipping address is collected by the API; use the same placeholder the existing checkout uses.
    private static readonly Address PlaceholderAddress =
        new("123 Main St.", "Kent", "OH", "United States", "44240");

    private readonly IRepository<Order> _orders;
    private readonly IReadRepository<CatalogItem> _catalog;
    private readonly IReadRepository<Order> _ordersRead;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IUriComposer _uriComposer;
    private readonly IOrderNotificationService _notifier;

    public ShopperOrderService(
        IRepository<Order> orders,
        IReadRepository<Order> ordersRead,
        IReadRepository<CatalogItem> catalog,
        IRepository<OrderNotification> notifications,
        IUriComposer uriComposer,
        IOrderNotificationService notifier)
    {
        _orders = orders;
        _ordersRead = ordersRead;
        _catalog = catalog;
        _notifications = notifications;
        _uriComposer = uriComposer;
        _notifier = notifier;
    }

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines,
        CancellationToken ct)
    {
        if (lines == null || lines.Count == 0)
        {
            throw new InvalidOrderRequestException("An order must contain at least one item.");
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalog.ListAsync(new CatalogItemsSpecification(ids), ct);
        var byId = catalogItems.ToDictionary(c => c.Id);

        var orderItems = new List<OrderItem>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new InvalidOrderRequestException(
                    $"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");
            }

            if (!byId.TryGetValue(line.CatalogItemId, out var catalogItem))
            {
                throw new InvalidOrderRequestException($"Catalog item {line.CatalogItemId} does not exist.");
            }

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        // Reuse the existing Order/OrderItem model rather than a parallel one.
        var order = new Order(buyerId, PlaceholderAddress, orderItems);
        await _orders.AddAsync(order, ct);

        // Best-effort: a failed notification never fails the order.
        await _notifier.SendOrderPlacedAsync(order, ct);

        return order.Id;
    }

    public async Task<IReadOnlyList<OrderNotificationsView>> GetMyOrdersAsync(string buyerId,
        CancellationToken ct)
    {
        var orders = await _ordersRead.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        var notifications = await _notifications.ListAsync(
            new OrderNotificationsByBuyerSpecification(buyerId), ct);

        // Reflect current provider outcomes on non-terminal notifications.
        await _notifier.RefreshOutcomesAsync(notifications, ct);

        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        return orders
            .OrderByDescending(o => o.OrderDate)
            .Select(o => BuildView(o,
                byOrder.TryGetValue(o.Id, out var list) ? list : new List<OrderNotification>()))
            .ToList();
    }

    public async Task<OrderNotificationsView?> GetOrderNotificationsAsync(string buyerId, int orderId,
        CancellationToken ct)
    {
        var order = await _ordersRead.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct);
        if (order == null || order.BuyerId != buyerId)
        {
            return null; // not the caller's order, or does not exist
        }

        var notifications = await _notifications.ListAsync(
            new OrderNotificationsByOrderSpecification(orderId), ct);
        await _notifier.RefreshOutcomesAsync(notifications, ct);

        return BuildView(order, notifications);
    }

    private static OrderNotificationsView BuildView(Order order, IReadOnlyList<OrderNotification> notifications)
    {
        var views = notifications
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new NotificationView(
                n.Id, n.Kind, n.DeliveryState, n.ProviderStatus, n.MessageSid, n.ProviderErrorCode,
                n.ProviderDateSent, n.ScheduledSendAt, n.ContentRedacted))
            .ToList();

        return new OrderNotificationsView(order.Id, order.Status.ToString(), order.OrderDate, order.Total(),
            views);
    }
}
