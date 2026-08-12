using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.OrderNotificationServiceTests;

public class NotificationOperations
{
    private readonly IRepository<Order> _orders = Substitute.For<IRepository<Order>>();
    private readonly IRepository<CatalogItem> _catalog = Substitute.For<IRepository<CatalogItem>>();
    private readonly IRepository<ContactNumber> _numbers = Substitute.For<IRepository<ContactNumber>>();
    private readonly IRepository<OrderNotification> _notifications = Substitute.For<IRepository<OrderNotification>>();
    private readonly IUriComposer _uriComposer = Substitute.For<IUriComposer>();
    private readonly ISmsGateway _gateway = Substitute.For<ISmsGateway>();
    private readonly IAppLogger<OrderNotificationService> _logger = Substitute.For<IAppLogger<OrderNotificationService>>();

    private OrderNotificationService Service()
    {
        _uriComposer.ComposePicUri(Arg.Any<string>()).Returns("pic.png");
        return new OrderNotificationService(_orders, _catalog, _numbers, _notifications, _uriComposer, _gateway, _logger);
    }

    private static OrderNotification Sent(int orderId, string buyerId, string sid, string body)
    {
        var n = new OrderNotification(orderId, buyerId, NotificationKind.OrderPlaced, "+12025550143", body);
        n.RecordSendOutcome(sid, "undelivered", 30034, null);
        return n;
    }

    [Fact]
    public async Task ResendUnderAnExistingKeyDoesNotSendAgain()
    {
        var original = Sent(1, "buyer", "SMoriginal", "body");
        var already = Sent(1, "buyer", "SMalready", "body");
        _notifications.GetByIdAsync(10, Arg.Any<CancellationToken>()).Returns(original);
        _notifications.FirstOrDefaultAsync(Arg.Any<NotificationByIdempotencyKeySpecification>(), Arg.Any<CancellationToken>())
            .Returns(already);

        var result = await Service().ResendAsync(10, "key-1");

        Assert.Same(already, result);
        await _gateway.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _notifications.DidNotReceive().AddAsync(Arg.Any<OrderNotification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResendUnderAFreshKeySendsAndStampsTheKey()
    {
        var original = Sent(1, "buyer", "SMoriginal", "body");
        _notifications.GetByIdAsync(10, Arg.Any<CancellationToken>()).Returns(original);
        _notifications.FirstOrDefaultAsync(Arg.Any<NotificationByIdempotencyKeySpecification>(), Arg.Any<CancellationToken>())
            .Returns((OrderNotification?)null);
        _gateway.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SmsSendResult(true, "SMresent", "queued", null, null));
        _notifications.AddAsync(Arg.Any<OrderNotification>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<OrderNotification>());

        var result = await Service().ResendAsync(10, "key-2");

        Assert.Equal("key-2", result.IdempotencyKey);
        Assert.Equal("SMresent", result.MessageSid);
        await _gateway.Received().SendAsync("+12025550143", "body", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RedactRemovesContentAtProviderAndLocally()
    {
        var n = Sent(1, "buyer", "SMredact", "sensitive body");
        _notifications.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(n);

        await Service().RedactNotificationContentAsync(5);

        await _gateway.Received().RedactContentAsync("SMredact", Arg.Any<CancellationToken>());
        Assert.True(n.ContentRedacted);
        Assert.Null(n.Body);
        await _notifications.Received().UpdateAsync(n, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlaceOrderStillSucceedsWhenTheNotificationSendFails()
    {
        var catalogItem = new CatalogItem(1, 1, "desc", "name", 5m, "pic");
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.GetSetMethod(nonPublic: true)!
            .Invoke(catalogItem, new object[] { 1 });

        _catalog.ListAsync(Arg.Any<CatalogItemsSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<CatalogItem> { catalogItem });
        _orders.AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<Order>());
        _numbers.ListAsync(Arg.Any<ContactNumbersByBuyerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber> { new("buyer", "+12025550143") });
        // The send blows up — the order operation must not.
        _gateway.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<SmsSendResult>>(_ => throw new System.Exception("boom"));

        var lines = new List<OrderLine> { new(1, 1) };

        var order = await Service().PlaceOrderAsync("buyer", lines, null);

        Assert.NotNull(order);
        await _orders.Received().AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
    }
}
