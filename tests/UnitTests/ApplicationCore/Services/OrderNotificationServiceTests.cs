using System;
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
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class OrderNotificationServiceTests
{
    private const string BuyerId = "buyer@example.com";

    private readonly IRepository<Order> _orders = Substitute.For<IRepository<Order>>();
    private readonly IRepository<CatalogItem> _catalogItems = Substitute.For<IRepository<CatalogItem>>();
    private readonly IRepository<ContactNumber> _contactNumbers = Substitute.For<IRepository<ContactNumber>>();
    private readonly IRepository<Notification> _notifications = Substitute.For<IRepository<Notification>>();
    private readonly ISmsGateway _gateway = Substitute.For<ISmsGateway>();
    private readonly IUriComposer _uriComposer = Substitute.For<IUriComposer>();
    private readonly IAppLogger<OrderNotificationService> _logger = Substitute.For<IAppLogger<OrderNotificationService>>();

    private OrderNotificationService Service() => new(
        _orders, _catalogItems, _contactNumbers, _notifications, _gateway, _uriComposer, _logger);

    private static Order OrderFor() =>
        new Order(BuyerId, new Address("s", "c", "st", "co", "z"), new List<OrderItem>());

    private void OneNumberOnFile() =>
        _contactNumbers.ListAsync(Arg.Any<ContactNumbersByBuyerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber> { new ContactNumber(BuyerId, "+15551234567") });

    [Fact]
    public async Task DispatchDoesNotFailWhenAMessageCannotBeSent()
    {
        _orders.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(OrderFor());
        OneNumberOnFile();
        _notifications.AddAsync(Arg.Any<Notification>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<Notification>());
        _gateway.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<SmsSendResult>(_ => throw new SmsGatewayException("unreachable"));
        _gateway.ScheduleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new SmsSendResult("SMfollow", "scheduled", null, null));

        var dispatched = await Service().DispatchAsync(1);

        Assert.True(dispatched); // the operation still succeeds
        await _notifications.Received().AddAsync(
            Arg.Is<Notification>(n => n.Kind == NotificationKind.OrderDispatched
                                      && n.DeliveryStatus == NotificationDeliveryStatus.SendFailed),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchQueuesAFollowUpWithTheProvider()
    {
        _orders.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(OrderFor());
        OneNumberOnFile();
        _notifications.AddAsync(Arg.Any<Notification>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<Notification>());
        _gateway.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SmsSendResult("SMnow", "queued", null, null));
        _gateway.ScheduleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new SmsSendResult("SMfollow", "scheduled", null, null));

        await Service().DispatchAsync(1);

        await _gateway.Received(1).ScheduleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await _notifications.Received().AddAsync(
            Arg.Is<Notification>(n => n.Kind == NotificationKind.DeliveryFollowUp
                                      && n.DeliveryStatus == NotificationDeliveryStatus.Scheduled),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelCallsOffAPendingScheduledFollowUp()
    {
        var followUp = new Notification(1, BuyerId, NotificationKind.DeliveryFollowUp, "+15551234567", "how did it go?");
        followUp.MarkScheduled("SMfollow", DateTimeOffset.UtcNow.AddDays(3));

        _orders.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(OrderFor());
        _contactNumbers.ListAsync(Arg.Any<ContactNumbersByBuyerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber>()); // no immediate cancel message needed
        _notifications.ListAsync(Arg.Any<NotificationsByOrderSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<Notification> { followUp });

        var cancelled = await Service().CancelAsync(1);

        Assert.True(cancelled);
        await _gateway.Received(1).CancelScheduledAsync("SMfollow", Arg.Any<CancellationToken>());
        Assert.Equal(NotificationDeliveryStatus.Canceled, followUp.DeliveryStatus);
    }

    [Fact]
    public async Task DispatchOfUnknownOrderReturnsFalse()
    {
        _orders.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns((Order?)null);

        Assert.False(await Service().DispatchAsync(123));
    }
}
