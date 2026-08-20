using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ShopperOrderService : IShopperOrderService
{
    private static Address NewDefaultShipTo() => new("N/A", "N/A", "N/A", "USA", "00000");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderFulfillment> _fulfillmentRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IOrderNotificationService _notificationService;

    public ShopperOrderService(
        IRepository<Order> orderRepository,
        IRepository<OrderFulfillment> fulfillmentRepository,
        IRepository<CatalogItem> itemRepository,
        IUriComposer uriComposer,
        IOrderNotificationService notificationService)
    {
        _orderRepository = orderRepository;
        _fulfillmentRepository = fulfillmentRepository;
        _itemRepository = itemRepository;
        _uriComposer = uriComposer;
        _notificationService = notificationService;
    }

    public async Task<OrderPlacementResult> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<CatalogOrderLine> lines,
        Address? shipToAddress,
        CancellationToken cancellationToken = default)
    {
        if (lines is null || lines.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one catalog item.", nameof(lines));
        }

        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new ArgumentException("Quantities must be greater than zero.", nameof(lines));
            }
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var catalogById = catalogItems.ToDictionary(c => c.Id);
        var missing = ids.Where(id => !catalogById.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
        {
            throw new KeyNotFoundException($"Catalog item(s) not found: {string.Join(", ", missing)}.");
        }

        var items = lines.Select(line =>
        {
            var catalogItem = catalogById[line.CatalogItemId];
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress ?? NewDefaultShipTo(), items);
        await _orderRepository.AddAsync(order, cancellationToken);
        var fulfillment = new OrderFulfillment(order.Id);
        await _fulfillmentRepository.AddAsync(fulfillment, cancellationToken);

        await _notificationService.NotifyOrderPlacedAsync(order, cancellationToken);
        var notifications = await _notificationService.ListForOrderAsync(order.Id, cancellationToken);
        return new OrderPlacementResult(order, fulfillment.Status, notifications);
    }

    public async Task<IReadOnlyList<ShopperOrder>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var statuses = await LoadStatusesAsync(orders.Select(o => o.Id).ToArray(), cancellationToken);
        return orders.Select(o => new ShopperOrder(o, statuses.GetValueOrDefault(o.Id, OrderStatus.Placed))).ToList();
    }

    public async Task<ShopperOrder?> GetOrderForCallerAsync(int orderId, string buyerId, bool isAdministrator, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            return null;
        }

        if (!isAdministrator && !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            return null;
        }

        var fulfillment = await _fulfillmentRepository.FirstOrDefaultAsync(
            new OrderFulfillmentByOrderIdSpec(orderId), cancellationToken);
        return new ShopperOrder(order, fulfillment?.Status ?? OrderStatus.Placed);
    }

    public async Task<ShopperOrder> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken)
            ?? throw new KeyNotFoundException("Order was not found.");
        var fulfillment = await GetFulfillmentAsync(orderId, cancellationToken);

        var alreadyDispatched = fulfillment.Status == OrderStatus.Dispatched;
        if (!alreadyDispatched)
        {
            fulfillment.MarkDispatched();
            await _fulfillmentRepository.SaveChangesAsync(cancellationToken);
            await _notificationService.NotifyOrderDispatchedAsync(order, cancellationToken);
        }

        return new ShopperOrder(order, fulfillment.Status);
    }

    public async Task<ShopperOrder> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken)
            ?? throw new KeyNotFoundException("Order was not found.");
        var fulfillment = await GetFulfillmentAsync(orderId, cancellationToken);

        var alreadyCancelled = fulfillment.Status == OrderStatus.Cancelled;
        if (!alreadyCancelled)
        {
            fulfillment.MarkCancelled();
            await _fulfillmentRepository.SaveChangesAsync(cancellationToken);
            await _notificationService.NotifyOrderCancelledAsync(order, cancellationToken);
        }

        return new ShopperOrder(order, fulfillment.Status);
    }

    private async Task<OrderFulfillment> GetFulfillmentAsync(int orderId, CancellationToken cancellationToken)
    {
        return await _fulfillmentRepository.FirstOrDefaultAsync(new OrderFulfillmentByOrderIdSpec(orderId), cancellationToken)
            ?? throw new KeyNotFoundException("Order was not found.");
    }

    private async Task<Dictionary<int, OrderStatus>> LoadStatusesAsync(int[] orderIds, CancellationToken cancellationToken)
    {
        if (orderIds.Length == 0)
        {
            return new Dictionary<int, OrderStatus>();
        }

        var fulfillments = await _fulfillmentRepository.ListAsync(
            new OrderFulfillmentsByOrderIdsSpec(orderIds), cancellationToken);
        return fulfillments.ToDictionary(f => f.ForOrderId, f => f.Status);
    }
}
