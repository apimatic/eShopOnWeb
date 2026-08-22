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
        IReadOnlyList<OrderLineRequest> items,
        Address? shipTo,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (items is null || items.Count == 0)
        {
            throw new InvalidPaymentRequestException("An order must contain at least one catalog item.");
        }

        var quantities = new Dictionary<int, int>();
        foreach (var line in items)
        {
            if (line.CatalogItemId <= 0)
            {
                throw new InvalidPaymentRequestException("Catalog item id must be a positive integer.");
            }

            if (line.Quantity <= 0)
            {
                throw new InvalidPaymentRequestException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");
            }

            quantities[line.CatalogItemId] = quantities.TryGetValue(line.CatalogItemId, out var existing)
                ? existing + line.Quantity
                : line.Quantity;
        }

        var catalogItems = await _itemRepository.ListAsync(
            new CatalogItemsSpecification(quantities.Keys.ToArray()), cancellationToken);

        foreach (var catalogItemId in quantities.Keys)
        {
            if (catalogItems.All(c => c.Id != catalogItemId))
            {
                throw new ResourceNotFoundException($"Catalog item {catalogItemId} was not found.");
            }
        }

        var orderItems = catalogItems.Select(catalogItem =>
        {
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, quantities[catalogItem.Id]);
        }).ToList();

        var address = shipTo ?? new Address("123 Main Street", "Seattle", "WA", "US", "98101");
        var order = new Order(buyerId, address, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);
        return order;
    }
}
