using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class CatalogOrderService : ICatalogOrderService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IOrderNotificationService _notificationService;

    public CatalogOrderService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IUriComposer uriComposer,
        IOrderNotificationService notificationService)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _uriComposer = uriComposer;
        _notificationService = notificationService;
    }

    public async Task<Order> PlaceAsync(
        string buyerId,
        IReadOnlyList<CatalogOrderItem> items,
        Address shippingAddress,
        CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (items == null || items.Count == 0)
        {
            throw new OrderStateException("An order must contain at least one catalog item.");
        }

        var grouped = items
            .Where(i => i.Quantity > 0)
            .GroupBy(i => i.CatalogItemId)
            .Select(g => new CatalogOrderItem(g.Key, g.Sum(x => x.Quantity)))
            .ToList();

        if (grouped.Count == 0)
        {
            throw new OrderStateException("An order must contain at least one catalog item.");
        }

        var ids = grouped.Select(i => i.CatalogItemId).ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), ct);
        if (catalogItems.Count != ids.Length)
        {
            throw new EntityNotFoundException("Catalog item");
        }

        var orderItems = grouped.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shippingAddress, orderItems);
        order = await _orderRepository.AddAsync(order, ct);

        await _notificationService.NotifyOrderPlacedAsync(order.Id, buyerId, order.Total(), ct);
        return order;
    }

    public async Task DispatchAsync(int orderId, CancellationToken ct)
    {
        var order = await GetByIdAsync(orderId, ct);
        try
        {
            order.MarkDispatched();
        }
        catch (System.InvalidOperationException ex)
        {
            throw new OrderStateException(ex.Message);
        }

        await _orderRepository.UpdateAsync(order, ct);
        await _notificationService.NotifyOrderDispatchedAsync(order.Id, order.BuyerId, ct);
    }

    public async Task CancelAsync(int orderId, CancellationToken ct)
    {
        var order = await GetByIdAsync(orderId, ct);
        try
        {
            order.MarkCancelled();
        }
        catch (System.InvalidOperationException ex)
        {
            throw new OrderStateException(ex.Message);
        }

        await _orderRepository.UpdateAsync(order, ct);
        await _notificationService.CancelPendingFollowUpsAsync(order.Id, ct);
        await _notificationService.NotifyOrderCancelledAsync(order.Id, order.BuyerId, ct);
    }

    public async Task<IReadOnlyList<Order>> ListForBuyerAsync(string buyerId, CancellationToken ct)
    {
        return await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
    }

    public async Task<Order> GetForBuyerAsync(int orderId, string buyerId, CancellationToken ct)
    {
        var order = await GetByIdAsync(orderId, ct);
        if (order.BuyerId != buyerId)
        {
            throw new EntityNotFoundException("Order");
        }

        return order;
    }

    public async Task<Order> GetByIdAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderByIdSpecification(orderId), ct);
        if (order == null)
        {
            throw new EntityNotFoundException("Order");
        }

        return order;
    }
}
