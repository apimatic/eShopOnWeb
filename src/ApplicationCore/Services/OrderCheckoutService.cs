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

public class OrderCheckoutService : IOrderCheckoutService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IUriComposer _uriComposer;

    public OrderCheckoutService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IUriComposer uriComposer)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _uriComposer = uriComposer;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<CatalogOrderLine> items,
        Address shipToAddress,
        CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(items, nameof(items));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));

        if (items.Count == 0)
        {
            throw new PaymentException(400, "An order must contain at least one catalog item.");
        }

        foreach (var line in items)
        {
            if (line.CatalogItemId < 1)
            {
                throw new PaymentException(400, "Catalog item id must be a positive integer.");
            }

            if (line.Quantity < 1)
            {
                throw new PaymentException(400, "Quantity must be at least 1.");
            }
        }

        var ids = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var byId = catalogItems.ToDictionary(c => c.Id);

        var missing = ids.Where(id => !byId.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            throw new PaymentException(400, $"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = new List<OrderItem>();
        foreach (var line in items)
        {
            var catalogItem = byId[line.CatalogItemId];
            var pictureUri = string.IsNullOrWhiteSpace(catalogItem.PictureUri)
                ? "placeholder"
                : _uriComposer.ComposePicUri(catalogItem.PictureUri);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipToAddress, orderItems);
        return await _orderRepository.AddAsync(order, cancellationToken);
    }
}
