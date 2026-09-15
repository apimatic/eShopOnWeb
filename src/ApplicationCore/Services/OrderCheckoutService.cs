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
        IReadOnlyList<PlaceOrderItem> items,
        Address shippingAddress,
        CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (items is null || items.Count == 0)
        {
            throw new PaymentException("At least one catalog item is required.", 400);
        }

        var ids = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var catalogById = catalogItems.ToDictionary(c => c.Id);

        var orderItems = new List<OrderItem>();
        foreach (var line in items)
        {
            if (line.Quantity <= 0)
            {
                throw new PaymentException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.", 400);
            }

            if (!catalogById.TryGetValue(line.CatalogItemId, out var catalogItem))
            {
                throw new PaymentException($"Catalog item {line.CatalogItemId} was not found.", 400);
            }

            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shippingAddress, orderItems);
        return await _orderRepository.AddAsync(order, cancellationToken);
    }
}
