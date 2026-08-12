using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.UnitTests.Builders;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.OrderNotificationServiceTests;

public class DispatchOrder
{
    private readonly IRepository<Order> _orders = Substitute.For<IRepository<Order>>();
    private readonly IRepository<CatalogItem> _catalogItems = Substitute.For<IRepository<CatalogItem>>();
    private readonly IRepository<ContactNumber> _contactNumbers = Substitute.For<IRepository<ContactNumber>>();
    private readonly IRepository<OrderNotification> _notifications = Substitute.For<IRepository<OrderNotification>>();
    private readonly ITwilioMessagingGateway _gateway = Substitute.For<ITwilioMessagingGateway>();
    private readonly IUriComposer _uriComposer = Substitute.For<IUriComposer>();
    private readonly IAppLogger<OrderNotificationService> _logger = Substitute.For<IAppLogger<OrderNotificationService>>();

    private OrderNotificationService CreateService() =>
        new(_orders, _catalogItems, _contactNumbers, _notifications, _gateway, _uriComposer, _logger);

    private void GivenOrder(Order order) =>
        _orders.FirstOrDefaultAsync(Arg.Any<OrderWithItemsByIdSpec>(), Arg.Any<CancellationToken>()).Returns(order);

    private void GivenContactNumber() =>
        _contactNumbers.ListAsync(Arg.Any<ContactNumbersByBuyerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber> { new("12345", "+15005550006") });

    private void GivenNoContactNumber() =>
        _contactNumbers.ListAsync(Arg.Any<ContactNumbersByBuyerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber>());

    [Fact]
    public async Task SendFailureNeverFailsTheDispatch()
    {
        var order = new OrderBuilder().WithDefaultValues();
        GivenOrder(order);
        GivenContactNumber();
        _gateway.SendMessageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new NotificationProviderException("provider down"));
        _gateway.ScheduleMessageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<System.DateTimeOffset>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new NotificationProviderException("provider down"));

        var result = await CreateService().DispatchOrderAsync(1);

        // The order is dispatched and persisted even though every message failed.
        Assert.Equal(OrderStatus.Dispatched, result.Status);
        await _orders.Received().UpdateAsync(order, Arg.Any<CancellationToken>());
        // Both the "on its way" message and the follow-up are still recorded (as failed) for the operator.
        await _notifications.Received(2).AddAsync(Arg.Any<OrderNotification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShopperWithNoNumberIsNotMessaged()
    {
        var order = new OrderBuilder().WithDefaultValues();
        GivenOrder(order);
        GivenNoContactNumber();

        var result = await CreateService().DispatchOrderAsync(1);

        Assert.Equal(OrderStatus.Dispatched, result.Status);
        await _gateway.DidNotReceive().SendMessageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _gateway.DidNotReceive().ScheduleMessageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<System.DateTimeOffset>(), Arg.Any<CancellationToken>());
        await _notifications.DidNotReceive().AddAsync(Arg.Any<OrderNotification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnknownOrderThrowsNotFound()
    {
        _orders.FirstOrDefaultAsync(Arg.Any<OrderWithItemsByIdSpec>(), Arg.Any<CancellationToken>()).Returns((Order?)null);

        await Assert.ThrowsAsync<EntityNotFoundException>(() => CreateService().DispatchOrderAsync(999));
    }
}
