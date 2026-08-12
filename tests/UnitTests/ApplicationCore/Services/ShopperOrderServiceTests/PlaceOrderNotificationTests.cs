using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.ShopperOrderServiceTests;

public class PlaceOrderNotificationTests
{
    private readonly IRepository<Order> _orders = Substitute.For<IRepository<Order>>();
    private readonly IRepository<CatalogItem> _items = Substitute.For<IRepository<CatalogItem>>();
    private readonly IRepository<ContactNumber> _contacts = Substitute.For<IRepository<ContactNumber>>();
    private readonly IRepository<Notification> _notifications = Substitute.For<IRepository<Notification>>();
    private readonly ISmsProvider _sms = Substitute.For<ISmsProvider>();
    private readonly IUriComposer _uriComposer = Substitute.For<IUriComposer>();
    private readonly IAppLogger<ShopperOrderService> _logger = Substitute.For<IAppLogger<ShopperOrderService>>();

    private ShopperOrderService CreateService() =>
        new(_orders, _items, _contacts, _notifications, _sms, _uriComposer, _logger);

    private static CatalogItem CatalogItemWithId(int id)
    {
        var item = new CatalogItem(1, 1, "desc", "Name", 10m, "pic.png");
        var setter = typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.GetSetMethod(nonPublic: true)!;
        setter.Invoke(item, new object[] { id });
        return item;
    }

    private void ArrangeCatalog()
    {
        _items.ListAsync(Arg.Any<CatalogItemsSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<CatalogItem> { CatalogItemWithId(1) });
        _uriComposer.ComposePicUri(Arg.Any<string>()).Returns("http://pic");
    }

    [Fact]
    public async Task SendFailureDoesNotFailThePlacementButRecordsFailedNotification()
    {
        ArrangeCatalog();
        _contacts.ListAsync(Arg.Any<ContactNumbersByOwnerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber> { new("buyer", "+15005550006") });
        _sms.When(x => x.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("provider down"));

        var result = await CreateService().PlaceOrderAsync("buyer", new[] { new OrderLineInput(1, 2) });

        // The order is still placed and the request still succeeds.
        Assert.True(result.Succeeded);
        await _orders.Received(1).AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
        // The failure is captured on a notification record so an operator can act on it.
        await _notifications.Received().AddAsync(
            Arg.Is<Notification>(n => n.Status == NotificationDeliveryStatus.Failed), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShopperWithNoNumberOnFileIsNotMessaged()
    {
        ArrangeCatalog();
        _contacts.ListAsync(Arg.Any<ContactNumbersByOwnerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber>());

        var result = await CreateService().PlaceOrderAsync("buyer", new[] { new OrderLineInput(1, 1) });

        Assert.True(result.Succeeded);
        await _sms.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _notifications.DidNotReceive().AddAsync(Arg.Any<Notification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnknownCatalogItemIsRejected()
    {
        _items.ListAsync(Arg.Any<CatalogItemsSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<CatalogItem>());

        var result = await CreateService().PlaceOrderAsync("buyer", new[] { new OrderLineInput(42, 1) });

        Assert.False(result.Succeeded);
        Assert.Equal(PlaceOrderError.ItemNotFound, result.Error);
        await _orders.DidNotReceive().AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
    }
}
