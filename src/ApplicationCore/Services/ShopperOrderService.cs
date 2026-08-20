using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ShopperOrderService : IShopperOrderService
{
    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IUriComposer _uriComposer;
    private readonly OrderNotificationPublisher _publisher;

    public ShopperOrderService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IUriComposer uriComposer,
        OrderNotificationPublisher publisher)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _uriComposer = uriComposer;
        _publisher = publisher;
    }

    public async Task<PlacedOrderResult> PlaceAsync(
        string buyerId,
        IReadOnlyList<CatalogOrderLine> lines,
        Address shippingAddress,
        CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shippingAddress, nameof(shippingAddress));
        Guard.Against.Null(lines, nameof(lines));

        if (lines.Count == 0)
        {
            throw new EmptyBasketOnCheckoutException("An order must contain at least one catalog item.");
        }

        foreach (var line in lines)
        {
            Guard.Against.NegativeOrZero(line.CatalogItemId, nameof(line.CatalogItemId));
            Guard.Against.NegativeOrZero(line.Quantity, nameof(line.Quantity));
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        foreach (var id in ids)
        {
            if (catalogItems.All(c => c.Id != id))
            {
                throw new CatalogItemNotFoundException(id);
            }
        }

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shippingAddress, orderItems);
        order = await _orders.AddAsync(order, cancellationToken);

        var notification = await _publisher.PublishAsync(
            order,
            NotificationKinds.OrderPlaced,
            $"Your eShopOnWeb order #{order.Id} has been placed.",
            sendAt: null,
            sourceNotificationId: null,
            cancellationToken);

        return new PlacedOrderResult(order, notification);
    }
}
