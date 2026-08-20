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
    private static readonly Address DefaultShippingAddress =
        new("123 Main St.", "Kent", "OH", "United States", "44240");

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

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<CatalogOrderItemRequest> items,
        CancellationToken cancellationToken = default)
    {
        if (items is null || items.Count == 0)
        {
            throw new OrderNotificationException("At least one catalog item is required.");
        }

        if (items.Any(i => i.Quantity < 1 || i.CatalogItemId < 1))
        {
            throw new OrderNotificationException("Each item must include a catalog item id and a quantity of at least 1.");
        }

        var catalogItemIds = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds), cancellationToken);
        var catalogById = catalogItems.ToDictionary(c => c.Id);

        var missing = catalogItemIds.Where(id => !catalogById.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
        {
            throw new OrderNotificationException("One or more catalog items were not found.", 404);
        }

        var orderItems = items.Select(item =>
        {
            var catalogItem = catalogById[item.CatalogItemId];
            var pictureUri = string.IsNullOrWhiteSpace(catalogItem.PictureUri)
                ? "placeholder.png"
                : _uriComposer.ComposePicUri(catalogItem.PictureUri);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            return new OrderItem(itemOrdered, catalogItem.Price, item.Quantity);
        }).ToList();

        var order = new Order(buyerId, DefaultShippingAddress, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);
        _logger.LogInformation("Placed order {OrderId} for buyer {BuyerId}.", order.Id, buyerId);

        await _notificationService.NotifyOrderPlacedAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetRequiredOrder(orderId, cancellationToken);
        order.MarkDispatched();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Dispatched order {OrderId}.", order.Id);

        await _notificationService.NotifyOrderDispatchedAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetRequiredOrder(orderId, cancellationToken);
        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Cancelled order {OrderId}.", order.Id);

        await _notificationService.NotifyOrderCancelledAsync(order, cancellationToken);
        return order;
    }

    public async Task<IReadOnlyList<Order>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var spec = new CustomerOrdersWithItemsSpecification(buyerId);
        return await _orderRepository.ListAsync(spec, cancellationToken);
    }

    public async Task<Order?> GetByIdForCallerAsync(
        int orderId,
        string buyerId,
        bool isAdministrator,
        CancellationToken cancellationToken = default)
    {
        var spec = new OrderWithItemsByIdSpec(orderId);
        var order = await _orderRepository.FirstOrDefaultAsync(spec, cancellationToken);
        if (order is null)
        {
            return null;
        }

        if (!isAdministrator && order.BuyerId != buyerId)
        {
            return null;
        }

        return order;
    }

    private async Task<Order> GetRequiredOrder(int orderId, CancellationToken cancellationToken)
    {
        var spec = new OrderWithItemsByIdSpec(orderId);
        var order = await _orderRepository.FirstOrDefaultAsync(spec, cancellationToken);
        if (order is null)
        {
            throw new OrderNotificationException("Order was not found.", 404);
        }

        return order;
    }
}
