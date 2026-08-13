using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Places an order for a shopper from catalog item ids and quantities, building the app's existing
/// <see cref="Order"/> / <see cref="OrderItem"/> aggregate (not a parallel one).
/// </summary>
public class OrderPlacementService : IOrderPlacementService
{
    // The notification feature does not collect a shipping address; orders placed through it use a
    // placeholder so the existing (address-required) Order model can be reused unchanged.
    private static readonly Address DefaultShipToAddress = new("N/A", "N/A", "N/A", "N/A", "00000");

    private readonly IRepository<Order> _orderRepository;
    private readonly IReadRepository<CatalogItem> _itemRepository;
    private readonly IUriComposer _uriComposer;

    public OrderPlacementService(
        IRepository<Order> orderRepository,
        IReadRepository<CatalogItem> itemRepository,
        IUriComposer uriComposer)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _uriComposer = uriComposer;
    }

    public async Task<Result<Order>> PlaceOrderAsync(
        string buyerId,
        IReadOnlyCollection<OrderLineRequest> lines,
        Address? shipToAddress,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        if (lines is null || lines.Count == 0)
        {
            return Result<Order>.Invalid(new List<ValidationError> { new() { ErrorMessage = "An order must contain at least one item." } });
        }

        if (lines.Any(l => l.Quantity <= 0))
        {
            return Result<Order>.Invalid(new List<ValidationError> { new() { ErrorMessage = "Every order line must have a quantity of at least 1." } });
        }

        var itemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(itemIds), cancellationToken);

        var missing = itemIds.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            return Result<Order>.Invalid(new List<ValidationError>
            {
                new() { ErrorMessage = $"Unknown catalog item id(s): {string.Join(", ", missing)}." }
            });
        }

        var orderItems = new List<OrderItem>();
        foreach (var line in lines)
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipToAddress ?? DefaultShipToAddress, orderItems);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        return Result<Order>.Success(order);
    }
}
