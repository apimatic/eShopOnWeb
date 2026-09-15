using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.UnitTests.Builders;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.OrderNotificationServiceTests;

public class OrderNotificationServiceTests
{
    private readonly IRepository<ContactNumber> _contacts = Substitute.For<IRepository<ContactNumber>>();
    private readonly IRepository<OrderNotification> _notifications = Substitute.For<IRepository<OrderNotification>>();
    private readonly ISmsProvider _sms = Substitute.For<ISmsProvider>();
    private readonly IAppLogger<OrderNotificationService> _logger = Substitute.For<IAppLogger<OrderNotificationService>>();

    private OrderNotificationService Service() => new(_contacts, _notifications, _sms, _logger);

    private void WithContactNumbers(params string[] numbers)
        => _contacts.ListAsync(Arg.Any<ISpecification<ContactNumber>>(), Arg.Any<CancellationToken>())
            .Returns(numbers.Select(n => new ContactNumber("12345", n)).ToList());

    private void WithAcceptedSends(string status = NotificationDeliveryStatus.Queued)
        => _sms.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(ci => new SmsSendResult("SM" + Guid.NewGuid().ToString("N"),
                ci.ArgAt<DateTimeOffset?>(2).HasValue ? NotificationDeliveryStatus.Scheduled : status,
                null, null, ci.ArgAt<DateTimeOffset?>(2)));

    [Fact]
    public async Task ShopperWithNoNumberIsNotMessaged()
    {
        WithContactNumbers(); // none
        var order = new OrderBuilder().WithDefaultValues();

        await Service().NotifyOrderPlacedAsync(order);

        await _sms.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
        await _notifications.DidNotReceive().AddAsync(Arg.Any<OrderNotification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlacedMessagesEachRegisteredNumber()
    {
        WithContactNumbers("+15551110000", "+15552220000");
        WithAcceptedSends();
        var order = new OrderBuilder().WithDefaultValues();

        await Service().NotifyOrderPlacedAsync(order);

        await _sms.Received(2).SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
        await _notifications.Received(2).AddAsync(Arg.Any<OrderNotification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendThatThrowsDoesNotFailTheOperation()
    {
        WithContactNumbers("+15551110000");
        _sms.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns<Task<SmsSendResult>>(_ => throw new InvalidOperationException("network down"));
        var order = new OrderBuilder().WithDefaultValues();

        // Must not throw.
        await Service().NotifyOrderPlacedAsync(order);

        // The failed notification is still persisted (recorded as send-failed).
        await _notifications.Received().AddAsync(Arg.Any<OrderNotification>(), Arg.Any<CancellationToken>());
        await _notifications.Received().UpdateAsync(
            Arg.Is<OrderNotification>(n => n.DeliveryStatus == NotificationDeliveryStatus.SendFailed), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchSendsAndSchedulesFollowUp()
    {
        WithContactNumbers("+15551110000");
        WithAcceptedSends();
        var order = new OrderBuilder().WithDefaultValues();

        await Service().NotifyOrderDispatchedAsync(order);

        // One immediate dispatched message and one scheduled (non-null sendAt) follow-up.
        await _sms.Received().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Is<DateTimeOffset?>(d => d == null), Arg.Any<CancellationToken>());
        await _sms.Received().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Is<DateTimeOffset?>(d => d.HasValue), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelCancelsPendingFollowUp()
    {
        WithContactNumbers("+15551110000");
        WithAcceptedSends();

        var followUp = new OrderNotification(1, "12345", NotificationType.DeliveryFollowUp, "+15551110000", null);
        followUp.RecordAccepted("SMsched", NotificationDeliveryStatus.Scheduled, null, null, DateTimeOffset.UtcNow.AddDays(3));
        _notifications.ListAsync(Arg.Any<ISpecification<OrderNotification>>(), Arg.Any<CancellationToken>())
            .Returns(new List<OrderNotification> { followUp });

        var order = new OrderBuilder().WithDefaultValues();
        await Service().NotifyOrderCancelledAsync(order);

        await _sms.Received().CancelScheduledAsync("SMsched", Arg.Any<CancellationToken>());
        Assert.Equal(NotificationDeliveryStatus.Canceled, followUp.DeliveryStatus);
    }

    [Fact]
    public async Task ResendUnderSameKeyReturnsExistingWithoutSending()
    {
        var existing = new OrderNotification(1, "12345", NotificationType.OrderPlaced, "+15551110000", null, "key-1");
        existing.RecordAccepted("SMexisting", NotificationDeliveryStatus.Queued, null, null, null);
        _notifications.ListAsync(Arg.Any<ISpecification<OrderNotification>>(), Arg.Any<CancellationToken>())
            .Returns(new List<OrderNotification> { existing });

        var result = await Service().ResendAsync(99, "key-1");

        Assert.Same(existing, result);
        await _sms.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResendUnderFreshKeySendsAgain()
    {
        _notifications.ListAsync(Arg.Any<ISpecification<OrderNotification>>(), Arg.Any<CancellationToken>())
            .Returns(new List<OrderNotification>());
        var source = new OrderNotification(1, "12345", NotificationType.OrderDispatched, "+15551110000", null);
        source.RecordAccepted("SMsrc", NotificationDeliveryStatus.Undelivered, 30006, "x", null);
        _notifications.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(source);
        WithAcceptedSends();

        var result = await Service().ResendAsync(5, "key-2");

        Assert.NotNull(result);
        Assert.Equal("key-2", result!.IdempotencyKey);
        await _sms.Received(1).SendAsync("+15551110000", Arg.Any<string>(), Arg.Is<DateTimeOffset?>(d => d == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResendUnknownNotificationReturnsNull()
    {
        _notifications.ListAsync(Arg.Any<ISpecification<OrderNotification>>(), Arg.Any<CancellationToken>())
            .Returns(new List<OrderNotification>());
        _notifications.GetByIdAsync(404, Arg.Any<CancellationToken>()).Returns((OrderNotification?)null);

        var result = await Service().ResendAsync(404, "key-3");

        Assert.Null(result);
    }

    [Fact]
    public async Task ReconcileMatchesBySid()
    {
        _sms.SenderNumber.Returns("+19998887777");
        var eShop = new OrderNotification(1, "12345", NotificationType.OrderPlaced, "+15551110000", null);
        eShop.RecordAccepted("SMmatch", NotificationDeliveryStatus.Delivered, null, null, null);
        _notifications.ListAsync(Arg.Any<ISpecification<OrderNotification>>(), Arg.Any<CancellationToken>())
            .Returns(new List<OrderNotification> { eShop });

        var providerMsgs = new List<ProviderMessage>
        {
            new("SMmatch", "+19998887777", "+15551110000", "delivered", "outbound-api", null, DateTimeOffset.UtcNow),
            new("SMonly",  "+19998887777", "+15551110000", "sent",      "outbound-api", null, DateTimeOffset.UtcNow)
        };
        _sms.ListOutboundMessagesAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(providerMsgs);

        var report = await Service().ReconcileAsync(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow);

        Assert.Single(report.Matched);
        Assert.Equal("SMmatch", report.Matched[0].MessageSid);
        Assert.Single(report.ProviderOnly);
        Assert.Equal("SMonly", report.ProviderOnly[0].MessageSid);
        Assert.Empty(report.EShopOnly);
    }
}
