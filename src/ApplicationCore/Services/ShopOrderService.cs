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

public class ShopOrderService : IShopOrderService
{
    private static readonly Address DefaultShipToAddress =
        new("123 Main St.", "Kent", "OH", "United States", "44240");

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IUriComposer _uriComposer;
    private readonly IOrderNotificationService _notifications;
    private readonly IAppLogger<ShopOrderService> _logger;

    public ShopOrderService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IUriComposer uriComposer,
        IOrderNotificationService notifications,
        IAppLogger<ShopOrderService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _uriComposer = uriComposer;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<Order> PlaceAsync(string buyerId, IReadOnlyList<ShopOrderLine> lines, CancellationToken cancellationToken)
    {
        if (lines is null || lines.Count == 0)
        {
            throw new EmptyBasketOnCheckoutException("An order must contain at least one item.");
        }

        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new System.ArgumentException("Quantity must be greater than zero.");
            }
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        if (catalogItems.Count != ids.Length)
        {
            throw new KeyNotFoundException("One or more catalog items were not found.");
        }

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, DefaultShipToAddress, orderItems);
        await _orders.AddAsync(order, cancellationToken);
        _logger.LogInformation("Placed order {OrderId} for buyer {BuyerId}", order.Id, buyerId);

        await SafeNotify(() => _notifications.NotifyOrderPlacedAsync(order.Id, buyerId, cancellationToken));
        return order;
    }

    public async Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await GetRequired(orderId, cancellationToken);
        order.MarkDispatched();
        await _orders.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Dispatched order {OrderId}", orderId);

        await SafeNotify(() => _notifications.NotifyOrderDispatchedAsync(order.Id, order.BuyerId, cancellationToken));
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await GetRequired(orderId, cancellationToken);
        order.MarkCancelled();
        await _orders.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Cancelled order {OrderId}", orderId);

        await SafeNotify(() => _notifications.CancelPendingFollowUpsAsync(order.Id, cancellationToken));
        await SafeNotify(() => _notifications.NotifyOrderCancelledAsync(order.Id, order.BuyerId, cancellationToken));
        return order;
    }

    public async Task<IReadOnlyList<Order>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken)
    {
        return await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
    }

    public Task<Order?> GetByIdAsync(int orderId, CancellationToken cancellationToken)
    {
        return _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
    }

    private async Task<Order> GetRequired(int orderId, CancellationToken cancellationToken)
    {
        var order = await GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            throw new KeyNotFoundException("Order not found.");
        }

        return order;
    }

    private async Task SafeNotify(Func<Task> notify)
    {
        try
        {
            await notify();
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Order notification failed and was ignored: {Message}", ex.Message);
        }
    }
}
