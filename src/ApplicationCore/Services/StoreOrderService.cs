using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class StoreOrderService : IStoreOrderService
{
    // The SMS feature is not concerned with shipping details; orders placed through the API use a
    // placeholder address, mirroring how the existing storefront checkout supplies one.
    private static readonly Address DefaultShipToAddress =
        new("123 Main St.", "Kent", "OH", "United States", "44240");

    private readonly IRepository<Order> _orderRepository;
    private readonly IReadRepository<CatalogItem> _catalogItems;
    private readonly IUriComposer _uriComposer;
    private readonly IOrderNotificationService _notificationService;
    private readonly IAppLogger<StoreOrderService> _logger;

    public StoreOrderService(
        IRepository<Order> orderRepository,
        IReadRepository<CatalogItem> catalogItems,
        IUriComposer uriComposer,
        IOrderNotificationService notificationService,
        IAppLogger<StoreOrderService> logger)
    {
        _orderRepository = orderRepository;
        _catalogItems = catalogItems;
        _uriComposer = uriComposer;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
            throw new ArgumentException("An order must contain at least one item.");
        if (lines.Any(l => l.Quantity < 1))
            throw new ArgumentException("Every item quantity must be at least 1.");

        var itemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(itemIds), cancellationToken);
        var catalogById = catalogItems.ToDictionary(c => c.Id);

        var missing = itemIds.Where(id => !catalogById.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
            throw new ArgumentException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogById[line.CatalogItemId];
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, DefaultShipToAddress, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);
        _logger.LogInformation("Placed order {OrderId} for buyer {BuyerId} with {LineCount} line(s).", order.Id, buyerId, orderItems.Count);

        // Tell the shopper their order was placed. A messaging failure must not fail the order.
        await _notificationService.NotifyOrderPlacedAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order?> DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
            return null;

        order.Dispatch();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Dispatched order {OrderId}.", order.Id);

        await _notificationService.NotifyOrderDispatchedAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order?> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
            return null;

        order.Cancel();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Cancelled order {OrderId}.", order.Id);

        await _notificationService.NotifyOrderCancelledAsync(order, cancellationToken);
        return order;
    }

    public async Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
    }

    public async Task<Order?> GetOrderForBuyerAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _orderRepository.FirstOrDefaultAsync(new OrderByIdAndBuyerSpecification(orderId, buyerId), cancellationToken);
    }
}
