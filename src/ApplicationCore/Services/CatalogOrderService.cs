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
    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IUriComposer _uriComposer;

    public CatalogOrderService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IUriComposer uriComposer)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _uriComposer = uriComposer;
    }

    public async Task<Order> PlaceAsync(
        string buyerId,
        IReadOnlyList<CatalogOrderLine> lines,
        CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
        {
            throw new ArgumentException("At least one catalog item is required.", nameof(lines));
        }

        foreach (var line in lines)
        {
            if (line.CatalogItemId <= 0 || line.Quantity <= 0)
            {
                throw new ArgumentException("Each line must have a catalog item id and a quantity greater than zero.");
            }
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var byId = catalogItems.ToDictionary(c => c.Id);

        var orderItems = new List<OrderItem>();
        foreach (var line in lines)
        {
            if (!byId.TryGetValue(line.CatalogItemId, out var catalogItem))
            {
                throw new KeyNotFoundException($"Catalog item {line.CatalogItemId} was not found.");
            }

            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var address = new Address("N/A", "N/A", "N/A", "N/A", "N/A");
        var order = new Order(buyerId, address, orderItems);
        return await _orders.AddAsync(order, cancellationToken);
    }
}
