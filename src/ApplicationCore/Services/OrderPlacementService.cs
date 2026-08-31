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

public class OrderPlacementService : IOrderPlacementService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IOrderNotificationService _notificationService;

    public OrderPlacementService(IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IOrderNotificationService notificationService)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _notificationService = notificationService;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<CatalogOrderItem> items,
        Address shipToAddress, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var catalogItems = await _itemRepository.ListAsync(
            new CatalogItemsSpecification(items.Select(i => i.CatalogItemId).ToArray()), ct);
        if (catalogItems.Count != items.Select(i => i.CatalogItemId).Distinct().Count())
        {
            throw new CatalogItemsNotFoundException("One or more catalog items do not exist.");
        }

        var orderItems = items.Select(i =>
        {
            var catalogItem = catalogItems.First(c => c.Id == i.CatalogItemId);
            return new OrderItem(
                new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri),
                catalogItem.Price,
                i.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);
        order = await _orderRepository.AddAsync(order, ct);

        await _notificationService.NotifyOrderPlacedAsync(order, ct);

        return order;
    }
}
