using System.Collections.Generic;
using System.Threading;
using Ardalis.Result;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class OrderNotificationServiceTests
{
    private readonly IRepository<Order> _orders = Substitute.For<IRepository<Order>>();
    private readonly IRepository<CatalogItem> _items = Substitute.For<IRepository<CatalogItem>>();
    private readonly IRepository<ContactNumber> _numbers = Substitute.For<IRepository<ContactNumber>>();
    private readonly IRepository<OrderNotification> _notifications = Substitute.For<IRepository<OrderNotification>>();
    private readonly ISmsSender _sms = Substitute.For<ISmsSender>();
    private readonly IUriComposer _uri = Substitute.For<IUriComposer>();
    private readonly IAppLogger<OrderNotificationService> _logger = Substitute.For<IAppLogger<OrderNotificationService>>();
    private readonly TwilioSettings _settings = new() { FromNumber = "+15550000000", MessagingServiceSid = "MG00000000000000000000000000000000" };

    private OrderNotificationService CreateService() =>
        new(_orders, _items, _numbers, _notifications, _sms, _uri, _settings, _logger);

    private static Order PlacedOrder(string buyerId = "buyer1", int id = 5)
    {
        var item = new OrderItem(new CatalogItemOrdered(1, "Widget", "pic.png"), 9.99m, 1);
        var order = new Order(buyerId, new Address("s", "c", "st", "co", "z"), new List<OrderItem> { item });
        // Simulate a persisted order (Id assigned by the store) so notifications can reference it.
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.SetValue(order, id);
        return order;
    }

    [Fact]
    public async System.Threading.Tasks.Task Dispatch_SucceedsAndRecordsFailure_WhenMessageCannotBeSent()
    {
        var order = PlacedOrder();
        _orders.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(order);
        _numbers.ListAsync(Arg.Any<ISpecification<ContactNumber>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber> { new("buyer1", "+12065550123") });
        _sms.SendAsync(Arg.Any<SmsSendRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new SmsProviderException(500, 30001));

        var result = await CreateService().DispatchAsync(5);

        // Operation still succeeds despite the send failures...
        Assert.True(result.IsSuccess);
        Assert.Equal(OrderStatus.Dispatched, order.Status);
        // ...and a notification is recorded marking the send as failed.
        await _notifications.Received().AddAsync(
            Arg.Is<OrderNotification>(n => n.SendFailed), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async System.Threading.Tasks.Task Dispatch_SchedulesFollowUpInTheFuture()
    {
        var order = PlacedOrder();
        _orders.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(order);
        _numbers.ListAsync(Arg.Any<ISpecification<ContactNumber>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber> { new("buyer1", "+12065550123") });
        _sms.SendAsync(Arg.Any<SmsSendRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci => new SmsMessageState { Sid = "SM1", Status = ci.Arg<SmsSendRequest>().SendAt is null ? "queued" : "scheduled" });

        await CreateService().DispatchAsync(5);

        // The follow-up is queued WITH the provider (SendAt set), not held by a local timer.
        await _sms.Received().SendAsync(
            Arg.Is<SmsSendRequest>(r => r.SendAt != null && r.SendAt > System.DateTimeOffset.UtcNow),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async System.Threading.Tasks.Task Cancel_CallsOffPendingScheduledFollowUp()
    {
        var order = PlacedOrder();
        order.MarkDispatched();
        _orders.GetByIdAsync(7, Arg.Any<CancellationToken>()).Returns(order);

        var followUp = new OrderNotification(7, "buyer1", NotificationKind.DeliveryFollowUp, "+12065550123");
        followUp.RecordProviderResult(new SmsMessageState { Sid = "SMsched", Status = "scheduled" });
        _notifications.ListAsync(Arg.Any<ISpecification<OrderNotification>>(), Arg.Any<CancellationToken>())
            .Returns(new List<OrderNotification> { followUp });
        _numbers.ListAsync(Arg.Any<ISpecification<ContactNumber>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber>());
        _sms.CancelScheduledAsync("SMsched", Arg.Any<CancellationToken>())
            .Returns(new SmsMessageState { Sid = "SMsched", Status = "canceled" });

        var result = await CreateService().CancelAsync(7);

        Assert.True(result.IsSuccess);
        await _sms.Received(1).CancelScheduledAsync("SMsched", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async System.Threading.Tasks.Task Resend_SameIdempotencyKey_DoesNotSendAgain()
    {
        var prior = new OrderNotification(1, "buyer1", NotificationKind.Resend, "+12065550123");
        prior.AssignIdempotencyKey("KEY-1");
        _notifications.FirstOrDefaultAsync(Arg.Any<ISpecification<OrderNotification>>(), Arg.Any<CancellationToken>())
            .Returns(prior);

        var result = await CreateService().ResendAsync(1, "KEY-1");

        Assert.True(result.IsSuccess);
        await _sms.DidNotReceive().SendAsync(Arg.Any<SmsSendRequest>(), Arg.Any<CancellationToken>());
        await _notifications.DidNotReceive().AddAsync(Arg.Any<OrderNotification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async System.Threading.Tasks.Task Resend_FreshIdempotencyKey_SendsAndPersists()
    {
        var original = new OrderNotification(1, "buyer1", NotificationKind.OrderPlaced, "+12065550123");
        _notifications.FirstOrDefaultAsync(Arg.Any<ISpecification<OrderNotification>>(), Arg.Any<CancellationToken>())
            .Returns((OrderNotification?)null);
        _notifications.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(original);
        _notifications.AddAsync(Arg.Any<OrderNotification>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<OrderNotification>());
        _sms.SendAsync(Arg.Any<SmsSendRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SmsMessageState { Sid = "SMnew", Status = "queued" });

        var result = await CreateService().ResendAsync(1, "KEY-2");

        Assert.True(result.IsSuccess);
        Assert.Equal("KEY-2", result.Value.IdempotencyKey);
        await _sms.Received(1).SendAsync(Arg.Any<SmsSendRequest>(), Arg.Any<CancellationToken>());
        await _notifications.Received(1).AddAsync(Arg.Any<OrderNotification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async System.Threading.Tasks.Task GetOrderNotifications_Forbidden_ForNonOwner()
    {
        var order = PlacedOrder("owner");
        _orders.GetByIdAsync(3, Arg.Any<CancellationToken>()).Returns(order);

        var result = await CreateService().GetOrderNotificationsAsync(3, "someone-else");

        Assert.Equal(ResultStatus.Forbidden, result.Status);
    }
}
