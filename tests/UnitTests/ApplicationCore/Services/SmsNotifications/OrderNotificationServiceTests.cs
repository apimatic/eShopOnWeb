using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SmsNotifications;

public class OrderNotificationServiceTests
{
    private const string Owner = "demouser@microsoft.com";
    private const string Number = "+15145551234";

    private readonly IRepository<Order> _orders = Substitute.For<IRepository<Order>>();
    private readonly IRepository<CatalogItem> _catalogItems = Substitute.For<IRepository<CatalogItem>>();
    private readonly IRepository<ContactNumber> _contactNumbers = Substitute.For<IRepository<ContactNumber>>();
    private readonly IRepository<OrderDelivery> _deliveries = Substitute.For<IRepository<OrderDelivery>>();
    private readonly IRepository<OrderNotification> _notifications = Substitute.For<IRepository<OrderNotification>>();
    private readonly ISmsSender _sms = Substitute.For<ISmsSender>();
    private readonly IUriComposer _uriComposer = Substitute.For<IUriComposer>();
    private readonly IAppLogger<OrderNotificationService> _logger = Substitute.For<IAppLogger<OrderNotificationService>>();
    private readonly List<OrderNotification> _added = new();

    public OrderNotificationServiceTests()
    {
        _uriComposer.ComposePicUri(Arg.Any<string>()).Returns("https://example/pic.png");
        _orders.AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Order>().WithId(42));
        _notifications.When(r => r.AddAsync(Arg.Any<OrderNotification>(), Arg.Any<CancellationToken>()))
            .Do(ci => _added.Add(ci.Arg<OrderNotification>()));
    }

    private OrderNotificationService CreateService() => new(
        _orders, _catalogItems, _contactNumbers, _deliveries, _notifications,
        _sms, _uriComposer, new NotificationSchedulingSettings { FollowUpDelay = TimeSpan.FromDays(3) }, _logger);

    private void HasNumberOnFile() =>
        _contactNumbers.ListAsync(Arg.Any<ContactNumbersByOwnerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber> { new(Owner, Number) });

    private void HasNoNumberOnFile() =>
        _contactNumbers.ListAsync(Arg.Any<ContactNumbersByOwnerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber>());

    private void CatalogHasItem(int id) =>
        _catalogItems.ListAsync(Arg.Any<CatalogItemsSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<CatalogItem> { new CatalogItem(1, 1, "desc", "Item", 9.99m, "pic.png").WithId(id) });

    [Fact]
    public async Task PlaceOrderWithNoNumberOnFilePlacesTheOrderAndSendsNothing()
    {
        HasNoNumberOnFile();
        CatalogHasItem(5);

        var order = await CreateService().PlaceOrderAsync(Owner, new[] { new OrderLine(5, 2) }, null);

        Assert.Equal(42, order.Id);
        await _deliveries.Received(1).AddAsync(Arg.Any<OrderDelivery>(), Arg.Any<CancellationToken>());
        await _sms.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Empty(_added);
    }

    [Fact]
    public async Task PlaceOrderStillSucceedsWhenTheMessageCannotBeSent()
    {
        HasNumberOnFile();
        CatalogHasItem(5);
        _sms.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new SmsProviderException("provider down"));

        var order = await CreateService().PlaceOrderAsync(Owner, new[] { new OrderLine(5, 1) }, null);

        Assert.Equal(42, order.Id); // the placement itself did not fail
        var notification = Assert.Single(_added);
        Assert.Equal(NotificationKind.OrderPlaced, notification.Kind);
        Assert.Equal(MessageDeliveryStatuses.SendFailed, notification.Status);
    }

    [Fact]
    public async Task PlaceOrderWithAnUnknownCatalogItemIsRejected()
    {
        HasNoNumberOnFile();
        CatalogHasItem(5);

        await Assert.ThrowsAsync<InvalidOrderRequestException>(
            () => CreateService().PlaceOrderAsync(Owner, new[] { new OrderLine(999, 1) }, null));
    }

    [Fact]
    public async Task DispatchQueuesADeliveryFollowUpWithTheProvider()
    {
        _deliveries.FirstOrDefaultAsync(Arg.Any<OrderDeliveryByOrderSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new OrderDelivery(42, Owner));
        HasNumberOnFile();
        _sms.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SmsSendResult("SMdispatch", MessageDeliveryStatuses.Queued));
        _sms.ScheduleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new SmsSendResult("SMfollowup", MessageDeliveryStatuses.Scheduled));

        var dispatched = await CreateService().DispatchAsync(42);

        Assert.True(dispatched);
        await _sms.Received(1).ScheduleAsync(Number, Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        Assert.Contains(_added, n => n.IsScheduledFollowUp
            && n.Kind == NotificationKind.DeliveryFollowUp
            && n.MessageSid == "SMfollowup"
            && n.Status == MessageDeliveryStatuses.Scheduled);
    }

    [Fact]
    public async Task DispatchOfAnUnknownOrderReturnsFalse()
    {
        _deliveries.FirstOrDefaultAsync(Arg.Any<OrderDeliveryByOrderSpecification>(), Arg.Any<CancellationToken>())
            .Returns((OrderDelivery?)null);

        Assert.False(await CreateService().DispatchAsync(123));
    }

    [Fact]
    public async Task CancelCallsOffANotYetSentFollowUpAtTheProvider()
    {
        var delivery = new OrderDelivery(42, Owner);
        delivery.MarkDispatched();
        _deliveries.FirstOrDefaultAsync(Arg.Any<OrderDeliveryByOrderSpecification>(), Arg.Any<CancellationToken>())
            .Returns(delivery);

        var pending = new OrderNotification(42, Owner, NotificationKind.DeliveryFollowUp, Number, isScheduledFollowUp: true);
        pending.RecordAccepted("SMfollowup", MessageDeliveryStatuses.Scheduled);
        _notifications.ListAsync(Arg.Any<PendingFollowUpByOrderSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<OrderNotification> { pending });
        HasNumberOnFile();
        _sms.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SmsSendResult("SMcancel", MessageDeliveryStatuses.Queued));

        var cancelled = await CreateService().CancelAsync(42);

        Assert.True(cancelled);
        await _sms.Received(1).CancelScheduledAsync("SMfollowup", Arg.Any<CancellationToken>());
        Assert.Equal(MessageDeliveryStatuses.Canceled, pending.Status);
    }
}
