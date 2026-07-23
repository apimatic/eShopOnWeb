using System;
using System.Linq;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BasketAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderService : IOrderService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IRepository<Basket> _basketRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IPublisher _publisher;
    private readonly IAppLogger<OrderService> _logger;

    public OrderService(IRepository<Basket> basketRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<Order> orderRepository,
        IUriComposer uriComposer,
        IPublisher publisher,
        IAppLogger<OrderService> logger)
    {
        _orderRepository = orderRepository;
        _uriComposer = uriComposer;
        _basketRepository = basketRepository;
        _itemRepository = itemRepository;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task CreateOrderAsync(int basketId, Address shippingAddress)
    {
        var basketSpec = new BasketWithItemsSpecification(basketId);
        var basket = await _basketRepository.FirstOrDefaultAsync(basketSpec);

        Guard.Against.Null(basket, nameof(basket));
        Guard.Against.EmptyBasketOnCheckout(basket.Items);

        var catalogItemsSpecification = new CatalogItemsSpecification(basket.Items.Select(item => item.CatalogItemId).ToArray());
        var catalogItems = await _itemRepository.ListAsync(catalogItemsSpecification);

        var items = basket.Items.Select(basketItem =>
        {
            var catalogItem = catalogItems.First(c => c.Id == basketItem.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            var orderItem = new OrderItem(itemOrdered, basketItem.UnitPrice, basketItem.Quantity);
            return orderItem;
        }).ToList();

        var order = new Order(basket.BuyerId, shippingAddress, items);

        order = await _orderRepository.AddAsync(order);

        await PublishOrderPlacedBestEffortAsync(order);
    }

    /// <summary>
    /// Announces the placed order in-process so subscribers (such as pay-as-you-go usage metering)
    /// can react.
    /// </summary>
    /// <remarks>
    /// Deliberately swallowing: the order has already been committed, and no downstream subscriber —
    /// billing included — may fail or roll back eShopOnWeb's order lifecycle. Failures are logged
    /// and nothing more, which is exactly the best-effort in-process guarantee this app offers.
    /// </remarks>
    private async Task PublishOrderPlacedBestEffortAsync(Order order)
    {
        try
        {
            await _publisher.Publish(new OrderPlaced(order.Id, order.BuyerId));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "In-process publication of OrderPlaced for order {OrderId} failed after the order was created: {Message}",
                order.Id,
                ex.Message);
        }
    }
}
