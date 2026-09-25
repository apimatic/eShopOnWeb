using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Places orders directly from catalog items (PublicApi has no basket of its own) and reports a
/// shopper's orders with payment state. Reuses the existing Order/OrderItem model; the order starts
/// awaiting payment. Amounts always come from catalog prices, never from the request.
/// </summary>
public class OrderCheckoutService : IOrderCheckoutService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IReadRepository<CatalogItem> _itemRepository;
    private readonly IReadRepository<Payment> _paymentRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPaymentGateway _gateway;

    public OrderCheckoutService(IRepository<Order> orderRepository,
        IReadRepository<CatalogItem> itemRepository,
        IReadRepository<Payment> paymentRepository,
        IUriComposer uriComposer,
        IPaymentGateway gateway)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentRepository = paymentRepository;
        _uriComposer = uriComposer;
        _gateway = gateway;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<PlaceOrderLine> lines,
        ShippingAddressInput? shipping, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
            throw new ArgumentException("An order must contain at least one item.", nameof(lines));
        if (lines.Any(l => l.Quantity <= 0))
            throw new ArgumentException("Item quantities must be positive.", nameof(lines));

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new ArgumentException($"Catalog item {line.CatalogItemId} was not found.", nameof(lines));
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var address = shipping is null
            ? new Address("N/A", "N/A", "N/A", "N/A", "00000")
            : new Address(shipping.Street, shipping.City, shipping.State, shipping.Country, shipping.ZipCode);

        var order = new Order(buyerId, address, items);
        return await _orderRepository.AddAsync(order, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderView>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var orders = await _orderRepository.ListAsync(new BuyerOrdersSpec(buyerId), cancellationToken);
        var payments = await _paymentRepository.ListAsync(new PaymentsByBuyerSpec(buyerId), cancellationToken);
        var byOrder = payments.ToDictionary(p => p.OrderId);

        return orders
            .Select(o => PaymentViewMapper.ToView(o, byOrder.TryGetValue(o.Id, out var p) ? p : null, _gateway.CurrencyCode))
            .ToList();
    }
}
