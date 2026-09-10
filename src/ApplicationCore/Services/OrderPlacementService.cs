using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Places an order straight from catalog item ids + quantities (no basket), reusing the existing
/// <see cref="Order"/> aggregate, and opens a <see cref="Payment"/> awaiting payment.
/// </summary>
public class OrderPlacementService : IOrderPlacementService
{
    // The base storefront has no shipping-address capture in this API surface; mirror the Web
    // checkout's placeholder address so the reused Order aggregate stays valid.
    private static readonly Address DefaultShipToAddress =
        new("123 Main St.", "Kent", "OH", "United States", "44240");

    private readonly IRepository<Order> _orderRepository;
    private readonly IReadRepository<CatalogItem> _itemRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IUriComposer _uriComposer;
    private readonly ICurrencyProvider _currencyProvider;

    public OrderPlacementService(
        IRepository<Order> orderRepository,
        IReadRepository<CatalogItem> itemRepository,
        IRepository<Payment> paymentRepository,
        IUriComposer uriComposer,
        ICurrencyProvider currencyProvider)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentRepository = paymentRepository;
        _uriComposer = uriComposer;
        _currencyProvider = currencyProvider;
    }

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineInput> lines, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
        {
            throw new PaymentException("An order must contain at least one line.");
        }
        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new PaymentException("Every order line must have a quantity of at least one.");
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), ct);

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new PaymentException($"Catalog item {line.CatalogItemId} was not found.", 404);

            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, DefaultShipToAddress, items);
        order = await _orderRepository.AddAsync(order, ct);

        var invoiceId = BuildInvoiceId(order.Id);
        var payment = new Payment(order.Id, buyerId, _currencyProvider.CurrencyCode, order.Total(), invoiceId);
        await _paymentRepository.AddAsync(payment, ct);

        return order.Id;
    }

    // A stable, unique-per-order external id used to reconcile eShop orders against PayPal.
    private static string BuildInvoiceId(int orderId) =>
        $"ESHOP-{orderId:D8}-{DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture)}";
}
