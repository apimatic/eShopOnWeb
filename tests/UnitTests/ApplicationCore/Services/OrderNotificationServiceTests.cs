using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.UnitTests.Builders;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class OrderNotificationServiceTests
{
    private readonly IRepository<OrderNotification> _notifications = Substitute.For<IRepository<OrderNotification>>();
    private readonly IRepository<NotificationResendRecord> _resendRecords = Substitute.For<IRepository<NotificationResendRecord>>();
    private readonly IContactNumberService _contacts = Substitute.For<IContactNumberService>();
    private readonly ITwilioMessagingClient _messaging = Substitute.For<ITwilioMessagingClient>();
    private readonly IAppLogger<OrderNotificationService> _logger = Substitute.For<IAppLogger<OrderNotificationService>>();

    private OrderNotificationService CreateSut() =>
        new(_notifications, _resendRecords, _contacts, _messaging, _logger);

    private static void SetId(BaseEntity entity, int id)
    {
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!
            .GetSetMethod(true)!
            .Invoke(entity, new object[] { id });
    }

    [Fact]
    public async Task NotifyOrderPlaced_DoesNotThrow_WhenProviderFails()
    {
        var order = new OrderBuilder().WithDefaultValues();
        SetId(order, 42);
        _contacts.GetPreferredAsync(order.BuyerId, Arg.Any<CancellationToken>())
            .Returns(new ContactNumber(order.BuyerId, "+15005550006"));
        _messaging.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<ProviderMessage>(_ => throw new InvalidOperationException("provider down"));

        var sut = CreateSut();
        await sut.NotifyOrderPlacedAsync(order);

        await _notifications.Received(1).AddAsync(Arg.Any<OrderNotification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotifyOrderPlaced_SkipsSend_WhenNoContactNumber()
    {
        var order = new OrderBuilder().WithDefaultValues();
        SetId(order, 7);
        _contacts.GetPreferredAsync(order.BuyerId, Arg.Any<CancellationToken>())
            .Returns((ContactNumber?)null);

        var sut = CreateSut();
        await sut.NotifyOrderPlacedAsync(order);

        await _messaging.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _notifications.Received(1).AddAsync(Arg.Any<OrderNotification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotifyOrderDispatched_QueuesFollowUpWithProvider()
    {
        var order = new OrderBuilder().WithDefaultValues();
        SetId(order, 9);
        _contacts.GetPreferredAsync(order.BuyerId, Arg.Any<CancellationToken>())
            .Returns(new ContactNumber(order.BuyerId, "+15005550006"));
        _messaging.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ProviderMessage("SM111", "queued", "body", null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "+1from"));
        _messaging.ScheduleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new ProviderMessage("SM222", "scheduled", "follow-up", null, null, null, DateTimeOffset.UtcNow, null));

        var sut = CreateSut();
        await sut.NotifyOrderDispatchedAsync(order);

        await _messaging.Received(1).ScheduleAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Is<DateTimeOffset>(t => t > DateTimeOffset.UtcNow.AddDays(2)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resend_ReturnsExistingResult_WhenIdempotencyKeyRepeats()
    {
        var resent = new OrderNotification(1, "buyer", OrderNotificationKind.OrderPlaced, "hello");
        SetId(resent, 11);

        _resendRecords.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<NotificationResendRecord>>(), Arg.Any<CancellationToken>())
            .Returns(new NotificationResendRecord(10, "key-1", 11));
        _notifications.GetByIdAsync(11, Arg.Any<CancellationToken>()).Returns(resent);

        var sut = CreateSut();
        var result = await sut.ResendAsync(10, "key-1");

        Assert.Equal(11, result.Id);
        await _messaging.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
