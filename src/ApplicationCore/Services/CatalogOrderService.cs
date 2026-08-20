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
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IRepository<Order> _orderRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IOrderNotificationService _orderNotificationService;

    private static readonly Address DefaultShipTo =
        new("123 Main Street", "Redmond", "WA", "United States", "98052");

    public CatalogOrderService(
        IRepository<CatalogItem> catalogItemRepository,
        IRepository<Order> orderRepository,
        IUriComposer uriComposer,
        IOrderNotificationService orderNotificationService)
    {
        _catalogItemRepository = catalogItemRepository;
        _orderRepository = orderRepository;
        _uriComposer = uriComposer;
        _orderNotificationService = orderNotificationService;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<CatalogOrderLine> lines, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(lines, nameof(lines));
        if (lines.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one item.", nameof(lines));
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var catalogById = catalogItems.ToDictionary(c => c.Id);

        var orderItems = new List<OrderItem>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new ArgumentException("Quantity must be greater than zero.");
            }

            if (!catalogById.TryGetValue(line.CatalogItemId, out var catalogItem))
            {
                throw new ArgumentException($"Catalog item {line.CatalogItemId} was not found.");
            }

            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, DefaultShipTo, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);

        try
        {
            await _orderNotificationService.NotifyOrderPlacedAsync(order, cancellationToken);
        }
        catch (Exception)
        {
            // A failed SMS must never fail placing the order.
        }

        return order;
    }
}
