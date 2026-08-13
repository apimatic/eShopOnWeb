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

public class OrderPlacementService : IOrderPlacementService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _catalogRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IOrderNotificationService _notificationService;

    public OrderPlacementService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogRepository,
        IUriComposer uriComposer,
        IOrderNotificationService notificationService)
    {
        _orderRepository = orderRepository;
        _catalogRepository = catalogRepository;
        _uriComposer = uriComposer;
        _notificationService = notificationService;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(lines, nameof(lines));
        if (lines.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one item.", nameof(lines));
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var itemsById = catalogItems.ToDictionary(c => c.Id);

        var orderItems = new List<OrderItem>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new ArgumentException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.", nameof(lines));
            }
            if (!itemsById.TryGetValue(line.CatalogItemId, out var catalogItem))
            {
                throw new ArgumentException($"Catalog item {line.CatalogItemId} does not exist.", nameof(lines));
            }

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        // Address is not part of the notification flow; use the app's existing default shipping address.
        var shipToAddress = new Address("123 Main St.", "Kent", "OH", "United States", "44240");
        var order = new Order(buyerId, shipToAddress, orderItems);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        // Best-effort: a messaging failure never fails order placement.
        await _notificationService.NotifyOrderPlacedAsync(order, cancellationToken);

        return order;
    }
}
