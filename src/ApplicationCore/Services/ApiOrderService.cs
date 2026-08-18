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

/// <summary>
/// Places and moves orders from the API surface, reusing the existing Order/OrderItem model, and
/// raises the appropriate shopper notification on each transition. Notification failures never fail
/// the order operation (the notification service guarantees that).
/// </summary>
public class ApiOrderService : IApiOrderService
{
    // Orders placed through the API have no address-capture step; use a neutral placeholder,
    // exactly as the storefront checkout does for this sample.
    private static readonly Address DefaultShipToAddress =
        new("123 Main St.", "Kent", "OH", "United States", "44240");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IUriComposer _uriComposer;
    private readonly ISmsNotificationService _notificationService;

    public ApiOrderService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IUriComposer uriComposer,
        ISmsNotificationService notificationService)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _uriComposer = uriComposer;
        _notificationService = notificationService;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
        {
            throw new InvalidOrderRequestException("An order must contain at least one item.");
        }

        // Merge duplicate lines by catalog item, and validate quantities.
        var merged = new Dictionary<int, int>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new InvalidOrderRequestException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");
            }

            merged[line.CatalogItemId] = merged.TryGetValue(line.CatalogItemId, out var q) ? q + line.Quantity : line.Quantity;
        }

        var catalogItems = await _itemRepository.ListAsync(
            new CatalogItemsSpecification(merged.Keys.ToArray()), cancellationToken);
        var byId = catalogItems.ToDictionary(c => c.Id);

        var orderItems = new List<OrderItem>();
        foreach (var (catalogItemId, quantity) in merged)
        {
            if (!byId.TryGetValue(catalogItemId, out var catalogItem))
            {
                throw new InvalidOrderRequestException($"Catalog item {catalogItemId} does not exist.");
            }

            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, quantity));
        }

        var order = new Order(buyerId, DefaultShipToAddress, orderItems);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        await _notificationService.NotifyOrderPlacedAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order?> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            return null;
        }

        order.MarkDispatched();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        await _notificationService.NotifyOrderDispatchedAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order?> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            return null;
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        await _notificationService.NotifyOrderCancelledAsync(order, cancellationToken);
        return order;
    }

    public async Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
    }

    public async Task<Order?> GetOrderForBuyerAsync(string buyerId, int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || order.BuyerId != buyerId)
        {
            return null;
        }

        return order;
    }
}
