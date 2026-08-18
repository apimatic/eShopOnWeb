using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Places and progresses orders through the existing order/order-item model, then hands off to the
/// notification service. Notification work is wrapped so a messaging failure can never fail the order.
/// </summary>
public class ShopperOrderService : IShopperOrderService
{
    private static readonly Address DefaultShipToAddress =
        new("N/A", "N/A", "N/A", "N/A", "00000");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IOrderNotificationService _notificationService;
    private readonly IAppLogger<ShopperOrderService> _logger;

    public ShopperOrderService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IUriComposer uriComposer,
        IOrderNotificationService notificationService,
        IAppLogger<ShopperOrderService> logger)
    {
        _orderRepository = orderRepository;
        _catalogItemRepository = catalogItemRepository;
        _uriComposer = uriComposer;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines, Address? shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines == null || lines.Count == 0)
            throw new EmptyOrderException("An order must contain at least one item.");

        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
                throw new EmptyOrderException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId);
            if (catalogItem == null)
                throw new CatalogItemNotFoundException(line.CatalogItemId);

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipToAddress ?? DefaultShipToAddress, items);
        await _orderRepository.AddAsync(order, cancellationToken);

        await SafelyNotifyAsync(() => _notificationService.NotifyOrderPlacedAsync(order, cancellationToken), order.Id, "placed");
        return order;
    }

    public async Task<Order?> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order == null)
            return null;

        order.Dispatch();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        await SafelyNotifyAsync(() => _notificationService.NotifyOrderDispatchedAsync(order, cancellationToken), order.Id, "dispatched");
        return order;
    }

    public async Task<Order?> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order == null)
            return null;

        order.Cancel();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        await SafelyNotifyAsync(() => _notificationService.NotifyOrderCancelledAsync(order, cancellationToken), order.Id, "cancelled");
        return order;
    }

    public async Task<IReadOnlyList<MyOrderView>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);

        var views = new List<MyOrderView>();
        foreach (var order in orders)
        {
            var notifications = await _notificationService.GetNotificationsForOrderAsync(order.Id, refresh: true, cancellationToken);
            views.Add(new MyOrderView(order, notifications));
        }
        return views;
    }

    public async Task<Order?> GetOrderForBuyerAsync(string buyerId, int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        return order != null && order.BuyerId == buyerId ? order : null;
    }

    private async Task SafelyNotifyAsync(System.Func<Task> notify, int orderId, string stage)
    {
        try
        {
            await notify();
        }
        catch (System.Exception ex)
        {
            // Defense in depth: the order operation must succeed even if notification work throws unexpectedly.
            _logger.LogError(ex, "Order {OrderId}: notification work for '{Stage}' failed but the order operation stands.", orderId, stage);
        }
    }
}
