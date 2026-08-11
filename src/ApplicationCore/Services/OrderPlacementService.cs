using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPlacementService : IOrderPlacementService
{
    // A placeholder shipping address for API-placed orders that don't supply one (this flow is about
    // payment, not fulfilment logistics); mirrors the hard-coded address used by the Web checkout.
    private static readonly ShippingAddress DefaultShipTo = new("123 Main St.", "Kent", "OH", "United States", "44240");

    private readonly IRepository<Order> _orderRepository;
    private readonly IReadRepository<CatalogItem> _catalogItemRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IUriComposer _uriComposer;
    private readonly PayPalSettings _settings;

    public OrderPlacementService(
        IRepository<Order> orderRepository,
        IReadRepository<CatalogItem> catalogItemRepository,
        IRepository<Payment> paymentRepository,
        IUriComposer uriComposer,
        PayPalSettings settings)
    {
        _orderRepository = orderRepository;
        _catalogItemRepository = catalogItemRepository;
        _paymentRepository = paymentRepository;
        _uriComposer = uriComposer;
        _settings = settings;
    }

    public async Task<PlacedOrder> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines,
        ShippingAddress? shipToAddress, CancellationToken cancellationToken = default)
    {
        if (lines is null || lines.Count == 0)
        {
            throw new PaymentException("An order must contain at least one item.");
        }
        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new PaymentException("Every order line must have a quantity of at least 1.");
        }

        var catalogItemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds), cancellationToken);
        var catalogById = catalogItems.ToDictionary(c => c.Id);

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            if (!catalogById.TryGetValue(line.CatalogItemId, out var catalogItem))
            {
                throw new PaymentException($"Catalog item {line.CatalogItemId} was not found.");
            }

            var pictureUri = _uriComposer.ComposePicUri(catalogItem.PictureUri ?? string.Empty);
            if (string.IsNullOrWhiteSpace(pictureUri))
            {
                pictureUri = "eCatalog-item-default.png";
            }

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            // Amounts come from catalog prices.
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var address = shipToAddress is null
            ? new Address(DefaultShipTo.Street, DefaultShipTo.City, DefaultShipTo.State, DefaultShipTo.Country, DefaultShipTo.ZipCode)
            : new Address(shipToAddress.Street, shipToAddress.City, shipToAddress.State, shipToAddress.Country, shipToAddress.ZipCode);

        var order = new Order(buyerId, address, items);
        await _orderRepository.AddAsync(order, cancellationToken);

        var payment = new Payment(order.Id, buyerId, order.Total(), _settings.Currency, OrderInvoice.New(order.Id));
        await _paymentRepository.AddAsync(payment, cancellationToken);

        return new PlacedOrder(order, payment);
    }
}
