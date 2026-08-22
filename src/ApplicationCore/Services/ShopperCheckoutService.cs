using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ShopperCheckoutService : IShopperCheckoutService
{
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<Order> _orderRepository;
    private readonly IUriComposer _uriComposer;

    public ShopperCheckoutService(
        IRepository<CatalogItem> itemRepository,
        IRepository<Order> orderRepository,
        IUriComposer uriComposer)
    {
        _itemRepository = itemRepository;
        _orderRepository = orderRepository;
        _uriComposer = uriComposer;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<(int CatalogItemId, int Quantity)> items,
        Address shipToAddress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentException(401, "The caller is not authenticated.");
        }

        if (items == null || items.Count == 0)
        {
            throw new PaymentException(400, "An order must contain at least one catalog item.");
        }

        foreach (var item in items)
        {
            if (item.CatalogItemId <= 0)
            {
                throw new PaymentException(400, "Catalog item id must be a positive integer.");
            }

            if (item.Quantity <= 0)
            {
                throw new PaymentException(400, "Quantity must be greater than zero.");
            }
        }

        var grouped = items
            .GroupBy(i => i.CatalogItemId)
            .Select(g => (CatalogItemId: g.Key, Quantity: g.Sum(x => x.Quantity)))
            .ToList();

        var catalogItems = await _itemRepository.ListAsync(
            new CatalogItemsSpecification(grouped.Select(g => g.CatalogItemId).ToArray()),
            cancellationToken);

        var missing = grouped
            .Select(g => g.CatalogItemId)
            .Except(catalogItems.Select(c => c.Id))
            .ToList();
        if (missing.Count > 0)
        {
            throw new PaymentException(400, $"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = grouped.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);
        return await _orderRepository.AddAsync(order, cancellationToken);
    }
}
