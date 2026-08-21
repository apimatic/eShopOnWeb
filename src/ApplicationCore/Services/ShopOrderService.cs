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

public class ShopOrderService : IShopOrderService
{
    private static readonly Address DefaultShipTo =
        new("123 Main St.", "Kent", "OH", "United States", "44240");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IUriComposer _uriComposer;

    public ShopOrderService(
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
        Address? shipToAddress,
        CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (items == null || items.Count == 0)
        {
            throw new PaymentException(400, "An order must contain at least one catalog item.");
        }

        var grouped = items
            .GroupBy(i => i.CatalogItemId)
            .Select(g => new { CatalogItemId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToList();

        if (grouped.Any(g => g.Quantity <= 0))
        {
            throw new PaymentException(400, "Each item quantity must be greater than zero.");
        }

        var ids = grouped.Select(g => g.CatalogItemId).ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), ct);
        var catalogById = catalogItems.ToDictionary(c => c.Id);

        foreach (var line in grouped)
        {
            if (!catalogById.ContainsKey(line.CatalogItemId))
            {
                throw new PaymentException(400, $"Catalog item {line.CatalogItemId} was not found.");
            }
        }

        var orderItems = grouped.Select(line =>
        {
            var catalogItem = catalogById[line.CatalogItemId];
            var pictureUri = string.IsNullOrWhiteSpace(catalogItem.PictureUri)
                ? "none"
                : _uriComposer.ComposePicUri(catalogItem.PictureUri);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress ?? DefaultShipTo, orderItems);
        return await _orderRepository.AddAsync(order, ct);
    }

    public async Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken ct)
    {
        var spec = new CustomerOrdersWithItemsSpecification(buyerId);
        return await _orderRepository.ListAsync(spec, ct);
    }

    public async Task<Order?> GetOrderForBuyerAsync(int orderId, string buyerId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), ct);
        if (order == null)
        {
            return null;
        }

        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new PaymentException(403, "This order does not belong to the signed-in shopper.");
        }

        return order;
    }

    public Task<Order?> GetOrderAsync(int orderId, CancellationToken ct)
    {
        return _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), ct);
    }
}
