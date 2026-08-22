using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.UnitTests.Builders;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.OrderNotificationServiceTests;

public class DispatchAndCancel
{
    private readonly IRepository<Order> _orders = Substitute.For<IRepository<Order>>();
    private readonly IRepository<OrderNotification> _notifications = Substitute.For<IRepository<OrderNotification>>();
    private readonly IContactNumberService _contactNumbers = Substitute.For<IContactNumberService>();
    private readonly ISmsNotificationGateway _sms = Substitute.For<ISmsNotificationGateway>();
    private readonly IAppLogger<OrderNotificationService> _logger = Substitute.For<IAppLogger<OrderNotificationService>>();

    private OrderNotificationService CreateService() =>
        new(_orders, _notifications, _contactNumbers, _sms, _logger);

    [Fact]
    public async Task DispatchSucceedsWhenSmsFails()
    {
        var order = new OrderBuilder().WithDefaultValues();
        _orders.FirstOrDefaultAsync(Arg.Any<OrderWithItemsByIdSpec>(), Arg.Any<CancellationToken>()).Returns(order);
        _contactNumbers.GetLatestForBuyerAsync(order.BuyerId, Arg.Any<CancellationToken>())
            .Returns(new ContactNumber(order.BuyerId, "+15555550100"));
        _sms.SendImmediateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<SmsSendResult>(_ => throw new SmsProviderException("down", 502));
        _sms.ScheduleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<System.DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns<SmsSendResult>(_ => throw new SmsProviderException("down", 502));

        var service = CreateService();
        await service.DispatchAsync(1, CancellationToken.None);

        Assert.Equal(OrderStatus.Dispatched, order.Status);
        await _orders.Received().UpdateAsync(order, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlaceNotificationIsSkippedWhenNoNumberOnFile()
    {
        var order = new OrderBuilder().WithDefaultValues();
        _contactNumbers.GetLatestForBuyerAsync(order.BuyerId, Arg.Any<CancellationToken>())
            .Returns((ContactNumber?)null);

        var service = CreateService();
        await service.NotifyOrderPlacedAsync(order, CancellationToken.None);

        await _sms.DidNotReceiveWithAnyArgs().SendImmediateAsync(default!, default!, default);
        await _notifications.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task CancelCancelsScheduledFollowUp()
    {
        var order = new OrderBuilder().WithDefaultValues();
        _orders.FirstOrDefaultAsync(Arg.Any<OrderWithItemsByIdSpec>(), Arg.Any<CancellationToken>()).Returns(order);

        var followUp = new OrderNotification(1, order.BuyerId, NotificationKind.DeliveryFollowUp, "How did it go?", 1, System.DateTimeOffset.UtcNow.AddDays(3));
        followUp.RecordSendResult("SM123", "scheduled", null, null);
        _notifications.ListAsync(Arg.Any<NotificationsByOrderSpec>(), Arg.Any<CancellationToken>())
            .Returns(new List<OrderNotification> { followUp });
        _sms.CancelScheduledAsync("SM123", Arg.Any<CancellationToken>())
            .Returns(new SmsSendResult(true, "SM123", "canceled", null, null));
        _contactNumbers.GetLatestForBuyerAsync(order.BuyerId, Arg.Any<CancellationToken>())
            .Returns((ContactNumber?)null);

        var service = CreateService();
        await service.CancelAsync(1, CancellationToken.None);

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        await _sms.Received().CancelScheduledAsync("SM123", Arg.Any<CancellationToken>());
        Assert.Equal("canceled", followUp.DeliveryStatus);
    }
}
