using System;
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

public class CheckoutService : ICheckoutService
{
    private static readonly Address DefaultShipTo = new("123 Main St.", "Kent", "OH", "United States", "44240");

    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<Order> _orderRepository;
    private readonly IUriComposer _uriComposer;

    public CheckoutService(
        IRepository<CatalogItem> itemRepository,
        IRepository<Order> orderRepository,
        IUriComposer uriComposer)
    {
        _itemRepository = itemRepository;
        _orderRepository = orderRepository;
        _uriComposer = uriComposer;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<CatalogLine> lines, Address? shipTo, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines == null || lines.Count == 0)
            throw new ArgumentException("An order must contain at least one catalog item.", nameof(lines));

        foreach (var line in lines)
        {
            if (line.CatalogItemId <= 0)
                throw new ArgumentException("Catalog item id must be positive.");
            if (line.Quantity <= 0)
                throw new ArgumentException("Quantity must be positive.");
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), ct);
        var catalogById = catalogItems.ToDictionary(c => c.Id);

        var orderItems = new List<OrderItem>();
        foreach (var line in lines)
        {
            if (!catalogById.TryGetValue(line.CatalogItemId, out var catalogItem))
                throw new EntityNotFoundException($"Catalog item {line.CatalogItemId} was not found.");

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipTo ?? DefaultShipTo, orderItems);
        return await _orderRepository.AddAsync(order, ct);
    }
}
