using System.Threading;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class OrderMessagingServiceResendTests
{
    private readonly IRepository<Order> _orders = Substitute.For<IRepository<Order>>();
    private readonly IRepository<CatalogItem> _items = Substitute.For<IRepository<CatalogItem>>();
    private readonly IReadRepository<ContactNumber> _contactNumbers = Substitute.For<IReadRepository<ContactNumber>>();
    private readonly IRepository<OrderNotification> _notifications = Substitute.For<IRepository<OrderNotification>>();
    private readonly ISmsGateway _gateway = Substitute.For<ISmsGateway>();
    private readonly IUriComposer _uriComposer = Substitute.For<IUriComposer>();
    private readonly IAppLogger<OrderMessagingService> _logger = Substitute.For<IAppLogger<OrderMessagingService>>();

    private OrderMessagingService CreateService() =>
        new(_orders, _items, _contactNumbers, _notifications, _gateway, _uriComposer, _logger);

    [Fact]
    public async Task ResendWithAlreadyUsedKeyDoesNotSendAgain()
    {
        var existing = new OrderNotification(1, "buyer", NotificationType.OrderCancelled, "+18255551588",
            idempotencyKey: "key-1");
        _notifications.FirstOrDefaultAsync(
                Arg.Any<ISpecification<OrderNotification>>(), Arg.Any<CancellationToken>())
            .Returns(existing);

        var result = await CreateService().ResendAsync(notificationId: 7, idempotencyKey: "key-1", CancellationToken.None);

        Assert.True(result.Found);
        Assert.True(result.WasDuplicate);
        await _gateway.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _notifications.DidNotReceive().AddAsync(Arg.Any<OrderNotification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResendWithFreshKeySendsOnce()
    {
        _notifications.FirstOrDefaultAsync(
                Arg.Any<ISpecification<OrderNotification>>(), Arg.Any<CancellationToken>())
            .Returns((OrderNotification?)null);
        var original = new OrderNotification(1, "buyer", NotificationType.OrderCancelled, "+18255551588");
        _notifications.GetByIdAsync(7, Arg.Any<CancellationToken>()).Returns(original);
        _notifications.AddAsync(Arg.Any<OrderNotification>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => (OrderNotification)callInfo[0]);
        _gateway.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SentMessage("SMxxxx", "queued", null, null, null));

        var result = await CreateService().ResendAsync(notificationId: 7, idempotencyKey: "key-2", CancellationToken.None);

        Assert.True(result.Found);
        Assert.False(result.WasDuplicate);
        await _gateway.Received(1).SendAsync("+18255551588", Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _notifications.Received(1).AddAsync(
            Arg.Is<OrderNotification>(n => n.IdempotencyKey == "key-2"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResendReturnsNotFoundForUnknownNotification()
    {
        _notifications.FirstOrDefaultAsync(
                Arg.Any<ISpecification<OrderNotification>>(), Arg.Any<CancellationToken>())
            .Returns((OrderNotification?)null);
        _notifications.GetByIdAsync(999, Arg.Any<CancellationToken>()).Returns((OrderNotification?)null);

        var result = await CreateService().ResendAsync(notificationId: 999, idempotencyKey: "key-3", CancellationToken.None);

        Assert.False(result.Found);
        await _gateway.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
