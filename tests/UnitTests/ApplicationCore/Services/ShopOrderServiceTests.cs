using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class ShopOrderServiceTests
{
    private readonly IRepository<Order> _orders = Substitute.For<IRepository<Order>>();
    private readonly IRepository<CatalogItem> _catalog = Substitute.For<IRepository<CatalogItem>>();
    private readonly IRepository<ContactNumber> _contacts = Substitute.For<IRepository<ContactNumber>>();
    private readonly IRepository<OrderNotification> _notifications = Substitute.For<IRepository<OrderNotification>>();
    private readonly ISmsGateway _sms = Substitute.For<ISmsGateway>();
    private readonly IUriComposer _uris = Substitute.For<IUriComposer>();
    private readonly IAppLogger<ShopOrderService> _logger = Substitute.For<IAppLogger<ShopOrderService>>();

    private ShopOrderService CreateService() =>
        new(_orders, _catalog, _contacts, _notifications, _sms, _uris, _logger);

    [Fact]
    public async Task PlaceOrderDoesNotSendWhenNoContactNumber()
    {
        var item = new CatalogItem(1, 1, "d", "n", 10m, "pic");
        typeof(CatalogItem).BaseType!.GetProperty("Id")!.SetValue(item, 1);
        _catalog.ListAsync(Arg.Any<CatalogItemsSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<CatalogItem> { item });
        _uris.ComposePicUri(Arg.Any<string>()).Returns("pic");
        _orders.AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var order = ci.Arg<Order>();
                typeof(Order).GetProperty("Id")!.SetValue(order, 42);
                return order;
            });
        _contacts.ListAsync(Arg.Any<ContactNumbersByBuyerSpec>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber>());

        var service = CreateService();
        var result = await service.PlaceOrderAsync("buyer", new[] { new CatalogOrderLine(1, 1) }, null, CancellationToken.None);

        Assert.Equal(42, result.Id);
        await _sms.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlaceOrderSucceedsWhenSmsFails()
    {
        var item = new CatalogItem(1, 1, "d", "n", 10m, "pic");
        typeof(CatalogItem).BaseType!.GetProperty("Id")!.SetValue(item, 1);
        _catalog.ListAsync(Arg.Any<CatalogItemsSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<CatalogItem> { item });
        _uris.ComposePicUri(Arg.Any<string>()).Returns("pic");
        _orders.AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var order = ci.Arg<Order>();
                typeof(Order).GetProperty("Id")!.SetValue(order, 7);
                return order;
            });
        _contacts.ListAsync(Arg.Any<ContactNumbersByBuyerSpec>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber> { new ContactNumber("buyer", "+15551234567") });
        _sms.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns<ProviderMessage>(_ => throw new InvalidOperationException("provider down"));

        var service = CreateService();
        var result = await service.PlaceOrderAsync("buyer", new[] { new CatalogOrderLine(1, 2) }, null, CancellationToken.None);

        Assert.Equal(7, result.Id);
        await _notifications.Received().AddAsync(Arg.Any<OrderNotification>(), Arg.Any<CancellationToken>());
    }
}
