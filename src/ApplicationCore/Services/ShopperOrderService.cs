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

public class ShopperOrderService : IShopperOrderService
{
    private static readonly Address DefaultShipToAddress = new("N/A", "N/A", "N/A", "N/A", "00000");

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IUriComposer _uriComposer;
    private readonly OrderSmsNotifier _notifier;

    public ShopperOrderService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<OrderNotification> notifications,
        IUriComposer uriComposer,
        OrderSmsNotifier notifier)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _notifications = notifications;
        _uriComposer = uriComposer;
        _notifier = notifier;
    }

    public async Task<ShopperOrderDetails> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<CatalogOrderItemRequest> items,
        Address? shipToAddress,
        CancellationToken cancellationToken = default)
    {
        if (items is null || items.Count == 0)
        {
            throw new BadRequestException("An order must include at least one catalog item.");
        }

        if (items.Any(i => i.CatalogItemId <= 0 || i.Quantity <= 0))
        {
            throw new BadRequestException("Each order item must include a catalog item id and a quantity greater than zero.");
        }

        var catalogItemIds = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(catalogItemIds), cancellationToken);
        if (catalogItems.Count != catalogItemIds.Length)
        {
            throw new BadRequestException("One or more catalog items were not found.");
        }

        var orderItems = items.Select(requestItem =>
        {
            var catalogItem = catalogItems.First(c => c.Id == requestItem.CatalogItemId);
            var pictureUri = string.IsNullOrWhiteSpace(catalogItem.PictureUri)
                ? "images/products/placeholder.png"
                : _uriComposer.ComposePicUri(catalogItem.PictureUri);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            return new OrderItem(itemOrdered, catalogItem.Price, requestItem.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress ?? DefaultShipToAddress, orderItems);
        order = await _orders.AddAsync(order, cancellationToken);

        var notifications = await _notifier.NotifyOrderPlacedAsync(order, cancellationToken);
        return new ShopperOrderDetails(order, notifications);
    }

    public async Task<IReadOnlyList<ShopperOrderDetails>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        if (orders.Count == 0)
        {
            return new List<ShopperOrderDetails>();
        }

        var notifications = await _notifications.ListAsync(
            new OrderNotificationsByOrderIdsSpec(orders.Select(o => o.Id)), cancellationToken);
        await _notifier.RefreshAllAsync(notifications, cancellationToken);

        return orders
            .Select(order => new ShopperOrderDetails(
                order,
                notifications.Where(n => n.OrderId == order.Id).ToList()))
            .ToList();
    }

    public async Task<ShopperOrderDetails?> GetOrderNotificationsAsync(
        string buyerId,
        int orderId,
        CancellationToken cancellationToken = default)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderByIdSpec(orderId), cancellationToken);
        if (order is null || !order.BelongsTo(buyerId))
        {
            return null;
        }

        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderIdSpec(orderId), cancellationToken);
        await _notifier.RefreshAllAsync(notifications, cancellationToken);
        return new ShopperOrderDetails(order, notifications);
    }
}
