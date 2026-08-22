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

public class CatalogCheckoutService : ICatalogCheckoutService
{
    private static readonly Address DefaultShippingAddress =
        new("123 Main Street", "Seattle", "WA", "USA", "98101");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IOrderNotificationService _notificationService;
    private readonly IAppLogger<CatalogCheckoutService> _logger;

    public CatalogCheckoutService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IUriComposer uriComposer,
        IOrderNotificationService notificationService,
        IAppLogger<CatalogCheckoutService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _uriComposer = uriComposer;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<CatalogOrderItem> items,
        Address? shippingAddress = null,
        CancellationToken cancellationToken = default)
    {
        if (items == null || items.Count == 0)
        {
            throw new ArgumentException("At least one catalog item is required.");
        }

        if (items.Any(i => i.Quantity <= 0))
        {
            throw new ArgumentException("Each item quantity must be greater than zero.");
        }

        var ids = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        if (catalogItems.Count != ids.Length)
        {
            throw new KeyNotFoundException("One or more catalog items were not found.");
        }

        var orderItems = items.Select(item =>
        {
            var catalogItem = catalogItems.First(c => c.Id == item.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, item.Quantity);
        }).ToList();

        var order = new Order(buyerId, shippingAddress ?? DefaultShippingAddress, orderItems);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        try
        {
            await _notificationService.NotifyOrderPlacedAsync(order, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Order {OrderId} was placed but notification failed: {Message}", order.Id, ex.Message);
        }

        return order;
    }
}
