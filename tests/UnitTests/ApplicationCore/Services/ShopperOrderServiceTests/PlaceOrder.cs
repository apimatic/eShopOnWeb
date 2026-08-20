using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.ShopperOrderServiceTests;

public class PlaceOrder
{
    private readonly IRepository<Order> _orders = Substitute.For<IRepository<Order>>();
    private readonly IRepository<CatalogItem> _catalog = Substitute.For<IRepository<CatalogItem>>();
    private readonly IUriComposer _uriComposer = Substitute.For<IUriComposer>();
    private readonly IOrderNotificationService _notifications = Substitute.For<IOrderNotificationService>();
    private readonly IAppLogger<ShopperOrderService> _logger = Substitute.For<IAppLogger<ShopperOrderService>>();

    [Fact]
    public async Task PlacesOrderAndNotifies()
    {
        var catalogItem = new CatalogItem(1, 1, "desc", "Mug", 8.5m, "pic.png");
        typeof(CatalogItem).GetProperty("Id")!.SetValue(catalogItem, 2);
        _catalog.ListAsync(Arg.Any<CatalogItemsSpecification>(), default)
            .Returns(new List<CatalogItem> { catalogItem });
        _uriComposer.ComposePicUri(Arg.Any<string>()).Returns("pic.png");
        _orders.AddAsync(Arg.Any<Order>(), default).Returns(ci => ci.Arg<Order>());

        var service = new ShopperOrderService(_orders, _catalog, _uriComposer, _notifications, _logger);
        var order = await service.PlaceAsync("buyer@example.com", new[] { new OrderLineRequest(2, 1) }, default);

        Assert.Equal("buyer@example.com", order.BuyerId);
        Assert.Equal(OrderStatus.Pending, order.Status);
        await _notifications.Received(1).NotifyOrderPlacedAsync(order, default);
    }

    [Fact]
    public async Task PlaceStillSucceedsWhenNotificationThrows()
    {
        var catalogItem = new CatalogItem(1, 1, "desc", "Mug", 8.5m, "pic.png");
        typeof(CatalogItem).GetProperty("Id")!.SetValue(catalogItem, 2);
        _catalog.ListAsync(Arg.Any<CatalogItemsSpecification>(), default)
            .Returns(new List<CatalogItem> { catalogItem });
        _uriComposer.ComposePicUri(Arg.Any<string>()).Returns("pic.png");
        _orders.AddAsync(Arg.Any<Order>(), default).Returns(ci => ci.Arg<Order>());
        _notifications.NotifyOrderPlacedAsync(Arg.Any<Order>(), default)
            .Throws(new OrderMessagingException("provider down", 503));

        var service = new ShopperOrderService(_orders, _catalog, _uriComposer, _notifications, _logger);

        var order = await service.PlaceAsync("buyer@example.com", new[] { new OrderLineRequest(2, 1) }, default);

        Assert.Equal("buyer@example.com", order.BuyerId);
        await _orders.Received(1).AddAsync(Arg.Any<Order>(), default);
    }
}
