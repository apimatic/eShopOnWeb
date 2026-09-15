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

public class ApiOrderService : IApiOrderService
{
    // The catalog/basket flow captures a shipping address; the API order flow carries only items,
    // so a system placeholder is used. Non-empty to satisfy the Order address constraints.
    private static readonly Address PlaceholderAddress = new("N/A", "N/A", "N/A", "N/A", "N/A");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly INotificationService _notificationService;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<ApiOrderService> _logger;

    public ApiOrderService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        INotificationService notificationService,
        IUriComposer uriComposer,
        IAppLogger<ApiOrderService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _notificationService = notificationService;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, CancellationToken cancellationToken)
    {
        if (lines is null || lines.Count == 0)
            return new PlaceOrderResult(null, "An order must contain at least one item.");
        if (lines.Any(l => l.Quantity <= 0))
            return new PlaceOrderResult(null, "Every item quantity must be greater than zero.");

        var requestedIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(requestedIds), cancellationToken);
        var catalogById = catalogItems.ToDictionary(c => c.Id);

        var missing = requestedIds.Where(id => !catalogById.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
            return new PlaceOrderResult(null, $"Unknown catalog item id(s): {string.Join(", ", missing)}.");

        var items = lines.Select(line =>
        {
            var catalogItem = catalogById[line.CatalogItemId];
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, PlaceholderAddress, items);
        await _orderRepository.AddAsync(order, cancellationToken);
        _logger.LogInformation("Placed order {OrderId} for {Buyer}.", order.Id, buyerId);

        await _notificationService.NotifyOrderPlacedAsync(order, cancellationToken);
        return new PlaceOrderResult(order, null);
    }

    public async Task<OrderTransitionResult> DispatchAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
            return new OrderTransitionResult(OrderTransitionOutcome.OrderNotFound, null, null);

        try
        {
            order.MarkDispatched();
        }
        catch (InvalidOperationException ex)
        {
            return new OrderTransitionResult(OrderTransitionOutcome.InvalidState, order, ex.Message);
        }

        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Dispatched order {OrderId}.", orderId);

        await _notificationService.NotifyOrderDispatchedAsync(order, cancellationToken);
        return new OrderTransitionResult(OrderTransitionOutcome.Succeeded, order, null);
    }

    public async Task<OrderTransitionResult> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
            return new OrderTransitionResult(OrderTransitionOutcome.OrderNotFound, null, null);

        try
        {
            order.MarkCancelled();
        }
        catch (InvalidOperationException ex)
        {
            return new OrderTransitionResult(OrderTransitionOutcome.InvalidState, order, ex.Message);
        }

        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Cancelled order {OrderId}.", orderId);

        await _notificationService.NotifyOrderCancelledAsync(order, cancellationToken);
        return new OrderTransitionResult(OrderTransitionOutcome.Succeeded, order, null);
    }

    public async Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken)
    {
        return await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
    }

    public async Task<Order?> GetOrderForBuyerAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || order.BuyerId != buyerId)
            return null;
        return order;
    }
}
