using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class CheckoutOrderService : ICheckoutOrderService
{
    private static readonly Address DefaultShipTo =
        new("123 Main St.", "Kent", "OH", "United States", "44240");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IUriComposer _uriComposer;

    public CheckoutOrderService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IUriComposer uriComposer)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _uriComposer = uriComposer;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<PlaceOrderItem> items, PlaceOrderAddress? shipTo)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (items == null || items.Count == 0)
        {
            throw new PaymentRequestException("An order must contain at least one catalog item.");
        }

        var grouped = items
            .GroupBy(i => i.CatalogItemId)
            .Select(g => new PlaceOrderItem(g.Key, g.Sum(x => x.Quantity)))
            .ToList();

        foreach (var item in grouped)
        {
            if (item.Quantity <= 0)
            {
                throw new PaymentRequestException($"Quantity for catalog item {item.CatalogItemId} must be greater than zero.");
            }
        }

        var catalogIds = grouped.Select(i => i.CatalogItemId).ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogIds));
        var missing = catalogIds.Except(catalogItems.Select(c => c.Id)).ToList();
        if (missing.Count > 0)
        {
            throw new EntityNotFoundException($"Catalog item(s) not found: {string.Join(", ", missing)}.");
        }

        var orderItems = grouped.Select(item =>
        {
            var catalogItem = catalogItems.First(c => c.Id == item.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, item.Quantity);
        }).ToList();

        var address = shipTo == null
            ? DefaultShipTo
            : new Address(shipTo.Street, shipTo.City, shipTo.State, shipTo.Country, shipTo.ZipCode);

        var order = new Order(buyerId, address, orderItems);
        return await _orderRepository.AddAsync(order);
    }
}
