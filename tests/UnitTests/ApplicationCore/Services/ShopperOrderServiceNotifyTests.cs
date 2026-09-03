using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class ShopperOrderServiceNotifyTests
{
    [Fact]
    public async Task PlaceOrderSucceedsWhenSendFails()
    {
        var orders = Substitute.For<IRepository<Order>>();
        var catalog = Substitute.For<IRepository<CatalogItem>>();
        var contacts = Substitute.For<IRepository<ShopperContactNumber>>();
        var notifications = Substitute.For<IRepository<OrderNotification>>();
        var messaging = Substitute.For<IMessagingGateway>();
        var uriComposer = Substitute.For<IUriComposer>();
        var logger = Substitute.For<IAppLogger<ShopperOrderService>>();

        var item = new CatalogItem(1, 1, "desc", "Mug", 8.5m, "http://img/1.png");
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.SetValue(item, 2);
        catalog.ListAsync(Arg.Any<CatalogItemsSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<CatalogItem> { item });
        uriComposer.ComposePicUri(Arg.Any<string>()).Returns("http://img/1.png");

        contacts.ListAsync(Arg.Any<ShopperContactNumbersSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<ShopperContactNumber> { new("buyer", "+15551234567") });

        orders.AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var order = call.Arg<Order>();
                typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.SetValue(order, 42);
                return order;
            });

        notifications.AddAsync(Arg.Any<OrderNotification>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<OrderNotification>());

        messaging.SendAsync(Arg.Any<SendMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<ProviderMessageSnapshot>>(_ => throw new HttpRequestException("boom"));

        var service = new ShopperOrderService(orders, catalog, contacts, notifications, messaging, uriComposer, logger);

        var order = await service.PlaceOrderAsync(
            "buyer",
            new[] { new PlaceOrderLine(2, 1) },
            new PlaceOrderAddress("s", "c", "st", "US", "00000"),
            CancellationToken.None);

        Assert.Equal(42, order.Id);
        await notifications.Received().UpdateAsync(
            Arg.Is<OrderNotification>(n => n.ProviderStatus == "send_failed"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResendReusesExistingNotificationForSameKey()
    {
        var orders = Substitute.For<IRepository<Order>>();
        var catalog = Substitute.For<IRepository<CatalogItem>>();
        var contacts = Substitute.For<IRepository<ShopperContactNumber>>();
        var notifications = Substitute.For<IRepository<OrderNotification>>();
        var messaging = Substitute.For<IMessagingGateway>();
        var uriComposer = Substitute.For<IUriComposer>();
        var logger = Substitute.For<IAppLogger<ShopperOrderService>>();

        var original = new OrderNotification(1, "buyer", NotificationKind.OrderPlaced, "+15551234567", "placed");
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.SetValue(original, 7);
        notifications.GetByIdAsync(7, Arg.Any<CancellationToken>()).Returns(original);

        var existingResend = new OrderNotification(1, "buyer", NotificationKind.Resend, "+15551234567", "placed");
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.SetValue(existingResend, 99);
        existingResend.MarkResend(7, "key-1");
        notifications.FirstOrDefaultAsync(Arg.Any<ResendByIdempotencyKeySpecification>(), Arg.Any<CancellationToken>())
            .Returns(existingResend);

        var service = new ShopperOrderService(orders, catalog, contacts, notifications, messaging, uriComposer, logger);

        var id = await service.ResendAsync(7, "key-1", CancellationToken.None);

        Assert.Equal(99, id);
        await messaging.DidNotReceive().SendAsync(Arg.Any<SendMessageRequest>(), Arg.Any<CancellationToken>());
    }
}
