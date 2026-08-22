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
    private static readonly Address DefaultShipTo = new("123 Main Street", "Seattle", "WA", "USA", "98101");

    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<Order> _orders;
    private readonly IUriComposer _uriComposer;

    public CatalogOrderService(
        IRepository<CatalogItem> catalogItems,
        IRepository<Order> orders,
        IUriComposer uriComposer)
    {
        _catalogItems = catalogItems;
        _orders = orders;
        _uriComposer = uriComposer;
    }

    public async Task<Order> PlaceAsync(
        string buyerId,
        IReadOnlyList<CatalogOrderLine> lines,
        Address? shipTo,
        CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines.Count == 0)
        {
            throw new EmptyCatalogOrderException();
        }

        foreach (var line in lines)
        {
            if (line.CatalogItemId <= 0 || line.Quantity <= 0)
            {
                throw new EmptyCatalogOrderException();
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
                throw new CatalogItemNotFoundException(line.CatalogItemId);
            }

            var pictureUri = _uriComposer.ComposePicUri(catalogItem.PictureUri);
            if (string.IsNullOrWhiteSpace(pictureUri))
            {
                pictureUri = catalogItem.PictureUri;
            }

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipTo ?? DefaultShipTo, orderItems);
        await _orders.AddAsync(order, cancellationToken);
        return order;
    }
}

public sealed record CatalogOrderLine(int CatalogItemId, int Quantity);

public class EmptyCatalogOrderException : System.Exception
{
    public EmptyCatalogOrderException() : base("The order must contain at least one catalog item with a positive quantity.")
    {
    }
}

public class CatalogItemNotFoundException : System.Exception
{
    public int CatalogItemId { get; }

    public CatalogItemNotFoundException(int catalogItemId)
        : base($"Catalog item {catalogItemId} was not found.")
    {
        CatalogItemId = catalogItemId;
    }
}
