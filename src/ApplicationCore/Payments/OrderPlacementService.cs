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

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed record OrderLine(int CatalogItemId, int Quantity);

/// <summary>
/// Places an order directly from catalog items (the API counterpart of basket checkout), using the
/// existing <see cref="Order"/>/<see cref="OrderItem"/> model. Prices always come from the catalog.
/// </summary>
public class OrderPlacementService
{
    public const int MaxQuantityPerLine = 1000;

    private readonly IRepository<Order> _orders;
    private readonly IReadRepository<CatalogItem> _catalogItems;
    private readonly IUriComposer _uriComposer;

    public OrderPlacementService(IRepository<Order> orders, IReadRepository<CatalogItem> catalogItems, IUriComposer uriComposer)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _uriComposer = uriComposer;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, Address shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
            throw new PaymentValidationException("An order needs at least one item.");
        if (lines.Any(l => l.Quantity is < 1 or > MaxQuantityPerLine))
            throw new PaymentValidationException($"Quantities must be between 1 and {MaxQuantityPerLine}.");

        var merged = lines
            .GroupBy(l => l.CatalogItemId)
            .Select(g => new OrderLine(g.Key, g.Sum(l => l.Quantity)))
            .ToList();
        if (merged.Any(l => l.Quantity > MaxQuantityPerLine))
            throw new PaymentValidationException($"Quantities must be between 1 and {MaxQuantityPerLine}.");

        var ids = merged.Select(l => l.CatalogItemId).ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var missing = ids.Except(catalogItems.Select(c => c.Id)).ToList();
        if (missing.Count > 0)
            throw new PaymentValidationException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");

        var items = merged.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, items);
        return await _orders.AddAsync(order, cancellationToken);
    }
}
