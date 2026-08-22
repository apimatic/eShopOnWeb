using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderWorkflowService : IOrderWorkflowService
{
    private static readonly Address DefaultShipTo =
        new("123 Main St.", "Kent", "OH", "United States", "44240");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IOrderNotificationService _notificationService;
    private readonly IAppLogger<OrderWorkflowService> _logger;

    public OrderWorkflowService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IUriComposer uriComposer,
        IOrderNotificationService notificationService,
        IAppLogger<OrderWorkflowService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _uriComposer = uriComposer;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<PlaceOrderItem> items, Address? shipToAddress = null)
    {
        if (items == null || items.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one item.");
        }

        foreach (var item in items)
        {
            if (item.CatalogItemId <= 0 || item.Quantity <= 0)
            {
                throw new ArgumentException("Each item must include a catalog item id and a quantity greater than zero.");
            }
        }

        var ids = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids));
        if (catalogItems.Count != ids.Length)
        {
            throw new ArgumentException("One or more catalog items were not found.");
        }

        var orderItems = items.Select(item =>
        {
            var catalogItem = catalogItems.First(c => c.Id == item.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, item.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress ?? DefaultShipTo, orderItems);
        await _orderRepository.AddAsync(order);

        try
        {
            await _notificationService.NotifyOrderPlacedAsync(order);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Order {OrderId} was placed but the placed notification could not be sent: {Message}", order.Id, ex.Message);
        }

        return order;
    }

    public async Task<Order> DispatchAsync(int orderId)
    {
        var order = await GetRequiredOrder(orderId);
        order.MarkDispatched();
        await _orderRepository.UpdateAsync(order);

        try
        {
            await _notificationService.NotifyOrderDispatchedAsync(order);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Order {OrderId} was dispatched but notifications could not be sent: {Message}", order.Id, ex.Message);
        }

        return order;
    }

    public async Task<Order> CancelAsync(int orderId)
    {
        var order = await GetRequiredOrder(orderId);
        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order);

        try
        {
            await _notificationService.NotifyOrderCancelledAsync(order);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Order {OrderId} was cancelled but notifications could not be sent: {Message}", order.Id, ex.Message);
        }

        return order;
    }

    public async Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersSpecification(buyerId));
        return orders;
    }

    public async Task<Order?> GetBuyerOrderAsync(string buyerId, int orderId)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId));
        if (order == null || order.BuyerId != buyerId)
        {
            return null;
        }

        return order;
    }

    public async Task<Order?> GetOrderAsync(int orderId)
    {
        return await _orderRepository.FirstOrDefaultAsync(new OrderByIdSpecification(orderId));
    }

    private async Task<Order> GetRequiredOrder(int orderId)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderByIdSpecification(orderId));
        if (order == null)
        {
            throw new KeyNotFoundException("Order was not found.");
        }

        return order;
    }
}
