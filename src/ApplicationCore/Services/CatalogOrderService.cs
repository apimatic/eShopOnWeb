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

public class CatalogOrderService : ICatalogOrderService
{
    private static readonly Address DefaultShipToAddress =
        new("123 Main St.", "Kent", "OH", "United States", "44240");

    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<Order> _orderRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IOrderNotificationService _notificationService;

    public CatalogOrderService(
        IRepository<CatalogItem> itemRepository,
        IRepository<Order> orderRepository,
        IUriComposer uriComposer,
        IOrderNotificationService notificationService)
    {
        _itemRepository = itemRepository;
        _orderRepository = orderRepository;
        _uriComposer = uriComposer;
        _notificationService = notificationService;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<CatalogOrderLine> lines,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one catalog item.", nameof(lines));
        }

        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new ArgumentException("Each catalog item quantity must be greater than zero.", nameof(lines));
            }
        }

        var catalogItemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds), cancellationToken);
        if (catalogItems.Count != catalogItemIds.Length)
        {
            throw new ArgumentException("One or more catalog items were not found.", nameof(lines));
        }

        var items = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var pictureUri = string.IsNullOrWhiteSpace(catalogItem.PictureUri)
                ? "images/products/placeholder.png"
                : _uriComposer.ComposePicUri(catalogItem.PictureUri);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, DefaultShipToAddress, items);
        await _orderRepository.AddAsync(order, cancellationToken);
        await _notificationService.NotifyOrderPlacedAsync(order, cancellationToken);
        return order;
    }
}
