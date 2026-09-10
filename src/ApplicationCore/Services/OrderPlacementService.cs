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

/// <summary>
/// Places an order directly from catalog items (the PublicApi path), reusing the existing
/// <see cref="Order"/>/<see cref="OrderItem"/> model, and creates the awaiting-payment
/// <see cref="OrderPayment"/> that the pay/fulfil/refund flow acts on.
/// </summary>
public class OrderPlacementService : IOrderPlacementService
{
    private static readonly Address PlaceholderAddress = new("N/A", "N/A", "N/A", "N/A", "N/A");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IReadRepository<CatalogItem> _catalogItemRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPaymentSettings _paymentSettings;

    public OrderPlacementService(
        IRepository<Order> orderRepository,
        IRepository<OrderPayment> paymentRepository,
        IReadRepository<CatalogItem> catalogItemRepository,
        IUriComposer uriComposer,
        IPaymentSettings paymentSettings)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _catalogItemRepository = catalogItemRepository;
        _uriComposer = uriComposer;
        _paymentSettings = paymentSettings;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines,
        CancellationToken cancellationToken = default)
    {
        if (lines is null || lines.Count == 0)
        {
            throw new InvalidPaymentRequestException("An order must contain at least one item.");
        }
        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new InvalidPaymentRequestException("Every order line must have a quantity of at least 1.");
        }

        var catalogItemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItemRepository.ListAsync(
            new CatalogItemsSpecification(catalogItemIds), cancellationToken);

        var orderItems = new List<OrderItem>();
        foreach (var line in lines)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new InvalidPaymentRequestException($"Catalog item {line.CatalogItemId} does not exist.");

            var pictureUri = string.IsNullOrEmpty(catalogItem.PictureUri)
                ? "eCatalog-item-default.png"
                : _uriComposer.ComposePicUri(catalogItem.PictureUri);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, PlaceholderAddress, orderItems);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        var payment = new OrderPayment(order.Id, buyerId, order.Total(), _paymentSettings.Currency);
        await _paymentRepository.AddAsync(payment, cancellationToken);

        return order;
    }
}
