using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.OrderNotificationServiceTests;

public class OrderNotificationServiceTests
{
    private const string BuyerId = "shopper@example.com";
    private const string CanonicalNumber = "+15551234567";

    private readonly IRepository<ContactNumber> _contactNumbers = Substitute.For<IRepository<ContactNumber>>();
    private readonly IRepository<OrderNotification> _notifications = Substitute.For<IRepository<OrderNotification>>();
    private readonly ISmsProvider _sms = Substitute.For<ISmsProvider>();
    private readonly IAppLogger<OrderNotificationService> _logger = Substitute.For<IAppLogger<OrderNotificationService>>();
    private readonly List<OrderNotification> _added = new();

    public OrderNotificationServiceTests()
    {
        _notifications.AddAsync(Arg.Do<OrderNotification>(n => _added.Add(n)), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<OrderNotification>());
    }

    private OrderNotificationService CreateService()
        => new(_contactNumbers, _notifications, _sms, _logger);

    private static Order CreateOrder()
        => new(BuyerId, new Address("1 Main St", "City", "ST", "US", "00000"),
            new List<OrderItem> { new(new CatalogItemOrdered(1, "Widget", "http://img/1.png"), 10m, 2) });

    private void GivenNumberOnFile()
    {
        _contactNumbers.ListAsync(Arg.Any<ContactNumbersByBuyerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber> { new(BuyerId, CanonicalNumber) });
    }

    [Fact]
    public async Task Placed_NoNumberOnFile_SendsNothing()
    {
        _contactNumbers.ListAsync(Arg.Any<ContactNumbersByBuyerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber>());

        await CreateService().NotifyOrderPlacedAsync(CreateOrder());

        await _sms.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Empty(_added);
    }

    [Fact]
    public async Task Placed_WithNumber_SendsAndRecordsAccepted()
    {
        GivenNumberOnFile();
        _sms.SendAsync(CanonicalNumber, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SmsSendResult(true, "SM123", "queued", null, null));

        await CreateService().NotifyOrderPlacedAsync(CreateOrder());

        var notification = Assert.Single(_added);
        Assert.Equal("SM123", notification.MessageSid);
        Assert.Equal("queued", notification.Status);
        Assert.Equal(OrderNotificationKind.OrderPlaced, notification.Kind);
    }

    [Fact]
    public async Task Placed_ProviderFails_DoesNotThrowAndRecordsFailure()
    {
        GivenNumberOnFile();
        _sms.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new SmsProviderException("The messaging provider could not be reached."));

        await CreateService().NotifyOrderPlacedAsync(CreateOrder()); // must not throw

        var notification = Assert.Single(_added);
        Assert.Equal(OrderNotificationStatus.SendFailed, notification.Status);
        Assert.Null(notification.MessageSid);
    }

    [Fact]
    public async Task Dispatched_SendsImmediateAndSchedulesFollowUpWithProvider()
    {
        GivenNumberOnFile();
        _sms.SendAsync(CanonicalNumber, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SmsSendResult(true, "SM111", "queued", null, null));
        _sms.ScheduleAsync(CanonicalNumber, Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new SmsSendResult(true, "SM222", "scheduled", null, null));

        var before = DateTimeOffset.UtcNow;
        await CreateService().NotifyOrderDispatchedAsync(CreateOrder());

        await _sms.Received(1).ScheduleAsync(CanonicalNumber, Arg.Any<string>(),
            Arg.Is<DateTimeOffset>(d => d > before.AddDays(2)), Arg.Any<CancellationToken>());

        Assert.Equal(2, _added.Count);
        var followUp = _added.Single(n => n.Kind == OrderNotificationKind.DeliveryFollowUp);
        Assert.Equal("SM222", followUp.MessageSid);
        Assert.Equal("scheduled", followUp.Status);
        Assert.NotNull(followUp.ScheduledFor);
    }

    [Fact]
    public async Task Cancelled_CallsOffPendingFollowUpAtProvider()
    {
        GivenNumberOnFile();
        _sms.SendAsync(CanonicalNumber, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SmsSendResult(true, "SM999", "queued", null, null));

        var followUp = new OrderNotification(1, BuyerId, CanonicalNumber, "how was it?", OrderNotificationKind.DeliveryFollowUp,
            DateTimeOffset.UtcNow.AddDays(3));
        followUp.MarkAccepted("SM222", "scheduled");
        _notifications.ListAsync(Arg.Any<NotificationsByOrderSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<OrderNotification> { followUp });
        _sms.CancelScheduledAsync("SM222", Arg.Any<CancellationToken>())
            .Returns(new SmsSendResult(true, "SM222", "canceled", null, null));

        await CreateService().NotifyOrderCancelledAsync(CreateOrder());

        await _sms.Received(1).CancelScheduledAsync("SM222", Arg.Any<CancellationToken>());
        Assert.Equal("canceled", followUp.Status);
    }

    [Fact]
    public async Task Resend_RepeatedKey_DoesNotSendAgain()
    {
        var existing = new OrderNotification(1, BuyerId, CanonicalNumber, "hello", OrderNotificationKind.Resend, null, "key-1");
        _notifications.FirstOrDefaultAsync(Arg.Any<NotificationByIdempotencyKeySpecification>(), Arg.Any<CancellationToken>())
            .Returns(existing);

        var result = await CreateService().ResendAsync(1, "key-1");

        Assert.NotNull(result);
        Assert.True(result.AlreadyExisted);
        Assert.Same(existing, result.Notification);
        await _sms.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Empty(_added);
    }

    [Fact]
    public async Task Resend_FreshKey_SendsAndRecordsNewNotification()
    {
        var original = new OrderNotification(1, BuyerId, CanonicalNumber, "hello", OrderNotificationKind.OrderPlaced);
        _notifications.FirstOrDefaultAsync(Arg.Any<NotificationByIdempotencyKeySpecification>(), Arg.Any<CancellationToken>())
            .Returns((OrderNotification?)null);
        _notifications.GetByIdAsync(1, Arg.Any<CancellationToken>())
            .Returns(original);
        _sms.SendAsync(CanonicalNumber, "hello", Arg.Any<CancellationToken>())
            .Returns(new SmsSendResult(true, "SM555", "queued", null, null));

        var result = await CreateService().ResendAsync(1, "key-2");

        Assert.NotNull(result);
        Assert.False(result.AlreadyExisted);
        var notification = Assert.Single(_added);
        Assert.Equal("key-2", notification.IdempotencyKey);
        Assert.Equal("SM555", notification.MessageSid);
        Assert.Equal(OrderNotificationKind.Resend, notification.Kind);
    }

    [Fact]
    public async Task Resend_UnknownNotification_ReturnsNull()
    {
        _notifications.FirstOrDefaultAsync(Arg.Any<NotificationByIdempotencyKeySpecification>(), Arg.Any<CancellationToken>())
            .Returns((OrderNotification?)null);
        _notifications.GetByIdAsync(99, Arg.Any<CancellationToken>())
            .Returns((OrderNotification?)null);

        var result = await CreateService().ResendAsync(99, "key-3");

        Assert.Null(result);
        await _sms.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
