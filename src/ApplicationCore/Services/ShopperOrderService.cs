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

public class ShopperOrderService : IShopperOrderService
{
    private static readonly Address DefaultShippingAddress = new("123 Main St.", "Kent", "OH", "United States", "44240");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IOrderNotificationService _notificationService;
    private readonly IAppLogger<ShopperOrderService> _logger;

    public ShopperOrderService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IUriComposer uriComposer,
        IOrderNotificationService notificationService,
        IAppLogger<ShopperOrderService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _uriComposer = uriComposer;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<CatalogItemQuantity> items, CancellationToken cancellationToken = default)
    {
        if (items == null || items.Count == 0)
        {
            throw new ArgumentException("At least one catalog item is required.");
        }

        foreach (var item in items)
        {
            if (item.CatalogItemId <= 0 || item.Quantity <= 0)
            {
                throw new ArgumentException("Each item must include a catalog item id and a positive quantity.");
            }
        }

        var ids = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        if (catalogItems.Count != ids.Length)
        {
            throw new ArgumentException("One or more catalog items were not found.");
        }

        var orderItems = items.Select(requested =>
        {
            var catalogItem = catalogItems.First(c => c.Id == requested.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, requested.Quantity);
        }).ToList();

        var order = new Order(buyerId, DefaultShippingAddress, orderItems);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        try
        {
            await _notificationService.NotifyOrderPlacedAsync(order, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Order placed notification failed for order {OrderId}: {Message}", order.Id, ex.Message);
        }

        return order;
    }

    public async Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderByIdSpec(orderId), cancellationToken)
            ?? throw new OrderNotFoundException();

        order.MarkDispatched();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        try
        {
            await _notificationService.NotifyOrderDispatchedAsync(order, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Order dispatched notification failed for order {OrderId}: {Message}", order.Id, ex.Message);
        }

        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderByIdSpec(orderId), cancellationToken)
            ?? throw new OrderNotFoundException();

        order.Cancel();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        try
        {
            await _notificationService.NotifyOrderCancelledAsync(order, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Order cancelled notification failed for order {OrderId}: {Message}", order.Id, ex.Message);
        }

        return order;
    }

    public async Task<IReadOnlyList<Order>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
    }

    public async Task<Order?> GetForBuyerAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderByIdSpec(orderId), cancellationToken);
        if (order == null || order.BuyerId != buyerId)
        {
            return null;
        }

        return order;
    }

    public async Task<Order?> GetByIdAsync(int orderId, CancellationToken cancellationToken = default)
    {
        return await _orderRepository.FirstOrDefaultAsync(new OrderByIdSpec(orderId), cancellationToken);
    }
}
