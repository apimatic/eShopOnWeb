using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPlacementService : IOrderPlacementService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IOrderNotificationService _notificationService;

    public OrderPlacementService(
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

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderItemRequest> items, Address shipToAddress)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (items == null || items.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one item.", nameof(items));
        }
        if (items.Any(i => i.Quantity <= 0))
        {
            throw new ArgumentException("Quantities must be positive.", nameof(items));
        }

        var catalogItemsSpecification = new CatalogItemsSpecification(items.Select(i => i.CatalogItemId).ToArray());
        var catalogItems = await _itemRepository.ListAsync(catalogItemsSpecification);

        var missingIds = items.Select(i => i.CatalogItemId).Except(catalogItems.Select(c => c.Id)).ToList();
        if (missingIds.Count > 0)
        {
            throw new ArgumentException($"Unknown catalog item id(s): {string.Join(", ", missingIds)}.", nameof(items));
        }

        var orderItems = items.Select(item =>
        {
            var catalogItem = catalogItems.First(c => c.Id == item.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, item.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);
        order = await _orderRepository.AddAsync(order);

        await _notificationService.NotifyOrderPlacedAsync(order);

        return order;
    }
}
