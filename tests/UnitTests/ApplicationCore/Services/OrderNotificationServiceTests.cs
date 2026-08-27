using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class OrderNotificationServiceTests
{
    private readonly IRepository<OrderNotification> _notifications = Substitute.For<IRepository<OrderNotification>>();
    private readonly IReadRepository<ContactNumber> _contacts = Substitute.For<IReadRepository<ContactNumber>>();
    private readonly IContactNumberService _contactNumbers = Substitute.For<IContactNumberService>();
    private readonly ISmsService _sms = Substitute.For<ISmsService>();
    private readonly IAppLogger<OrderNotificationService> _logger = Substitute.For<IAppLogger<OrderNotificationService>>();

    private OrderNotificationService Sut => new(_notifications, _contacts, _contactNumbers, _sms, _logger);

    private static Order Order(int id = 1, string buyerId = "demouser@microsoft.com")
    {
        var order = new Order(buyerId, new Address("123 Main St", "Kent", "OH", "United States", "44240"),
            new List<OrderItem>());
        return order;
    }

    [Fact]
    public async Task NotifyOrderPlaced_NoNumberOnFile_SendsNothing()
    {
        _contactNumbers.GetPrimaryAsync(Arg.Any<string>()).Returns((ContactNumber?)null);

        await Sut.NotifyOrderPlacedAsync(Order());

        await _sms.DidNotReceive().SendSmsAsync(Arg.Any<string>(), Arg.Any<string>());
        await _notifications.DidNotReceive().AddAsync(Arg.Any<OrderNotification>());
    }

    [Fact]
    public async Task NotifyOrderPlaced_SendFails_RecordsFailure_AndDoesNotThrow()
    {
        _contactNumbers.GetPrimaryAsync(Arg.Any<string>()).Returns(new ContactNumber("buyer", "+15550002222"));
        _sms.SendSmsAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns<Task<SmsSendResult>>(_ => throw new SmsProviderException("rejected", System.Net.HttpStatusCode.BadRequest));

        await Sut.NotifyOrderPlacedAsync(Order()); // must not throw

        await _notifications.Received(1).AddAsync(
            Arg.Is<OrderNotification>(n => n.Status == NotificationStatuses.SendFailed),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotifyOrderDispatched_SendsDispatch_AndSchedulesFollowUp()
    {
        _contactNumbers.GetPrimaryAsync(Arg.Any<string>()).Returns(new ContactNumber("buyer", "+15550002222"));
        _sms.SendSmsAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(new SmsSendResult("SM1", "queued"));
        _sms.ScheduleSmsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>())
            .Returns(new SmsSendResult("SM2", "scheduled"));

        await Sut.NotifyOrderDispatchedAsync(Order());

        await _sms.Received(1).SendSmsAsync("+15550002222", Arg.Any<string>());
        await _sms.Received(1).ScheduleSmsAsync("+15550002222", Arg.Any<string>(),
            Arg.Is<DateTimeOffset>(d => d > DateTimeOffset.UtcNow.AddDays(2)));
        await _notifications.Received(2).AddAsync(Arg.Any<OrderNotification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotifyOrderCancelled_CancelsPendingFollowUp()
    {
        _contactNumbers.GetPrimaryAsync(Arg.Any<string>()).Returns(new ContactNumber("buyer", "+15550002222"));
        _sms.SendSmsAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(new SmsSendResult("SM3", "queued"));

        var followUp = new OrderNotification(1, "buyer", 5, NotificationType.DeliveryFollowUp, "follow up", DateTimeOffset.UtcNow.AddDays(3));
        followUp.MarkProviderAccepted("SM-sched", NotificationStatuses.Scheduled);
        _notifications.ListAsync(Arg.Any<ScheduledFollowUpsForOrderSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<OrderNotification> { followUp });

        await Sut.NotifyOrderCancelledAsync(Order());

        await _sms.Received(1).CancelScheduledSmsAsync("SM-sched");
        Assert.Equal(NotificationStatuses.Canceled, followUp.Status);
    }

    [Fact]
    public async Task Resend_SameIdempotencyKey_ReturnsOriginalWithoutSendingAgain()
    {
        var original = new OrderNotification(1, "buyer", 5, NotificationType.OrderPlaced, "placed");
        var existingResend = new OrderNotification(1, "buyer", 5, NotificationType.OrderPlaced, "placed",
            resendOfNotificationId: 7, idempotencyKey: "key-1");
        _notifications.GetByIdAsync(7, Arg.Any<CancellationToken>()).Returns(original);
        _notifications.FirstOrDefaultAsync(Arg.Any<ResendByIdempotencyKeySpecification>(), Arg.Any<CancellationToken>())
            .Returns(existingResend);

        var result = await Sut.ResendAsync(7, "key-1");

        Assert.Same(existingResend, result);
        await _sms.DidNotReceive().SendSmsAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Resend_ContentDisposed_Rejected()
    {
        var original = new OrderNotification(1, "buyer", 5, NotificationType.OrderPlaced, "placed");
        original.MarkContentDisposed();
        _notifications.GetByIdAsync(7, Arg.Any<CancellationToken>()).Returns(original);

        await Assert.ThrowsAsync<BadRequestException>(() => Sut.ResendAsync(7, "key-2"));
        await _sms.DidNotReceive().SendSmsAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Resend_NumberNoLongerRegistered_Rejected()
    {
        var original = new OrderNotification(1, "buyer", 5, NotificationType.OrderPlaced, "placed");
        _notifications.GetByIdAsync(7, Arg.Any<CancellationToken>()).Returns(original);
        _contacts.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns((ContactNumber?)null);

        await Assert.ThrowsAsync<BadRequestException>(() => Sut.ResendAsync(7, "key-3"));
        await _sms.DidNotReceive().SendSmsAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Resend_FreshKey_SendsAndRecordsNewNotification()
    {
        var original = new OrderNotification(1, "buyer", 5, NotificationType.OrderPlaced, "placed");
        _notifications.GetByIdAsync(7, Arg.Any<CancellationToken>()).Returns(original);
        _contacts.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(new ContactNumber("buyer", "+15550002222"));
        _sms.SendSmsAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(new SmsSendResult("SM-new", "queued"));

        var result = await Sut.ResendAsync(7, "key-4");

        Assert.Equal("SM-new", result.ProviderMessageSid);
        Assert.Equal(7, result.ResendOfNotificationId);
        Assert.Equal("key-4", result.IdempotencyKey);
        await _notifications.Received(1).AddAsync(result, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteContent_RedactsAtProvider_ThenDisposesLocally()
    {
        var notification = new OrderNotification(1, "buyer", 5, NotificationType.OrderPlaced, "placed");
        notification.MarkProviderAccepted("SM9", NotificationStatuses.Delivered);
        _notifications.GetByIdAsync(9, Arg.Any<CancellationToken>()).Returns(notification);

        await Sut.DeleteContentAsync(9);

        await _sms.Received(1).RedactMessageBodyAsync("SM9");
        Assert.True(notification.ContentDisposed);
    }

    [Fact]
    public async Task DeleteContent_ProviderFails_KeepsLocalContent()
    {
        var notification = new OrderNotification(1, "buyer", 5, NotificationType.OrderPlaced, "placed");
        notification.MarkProviderAccepted("SM9", NotificationStatuses.Delivered);
        _notifications.GetByIdAsync(9, Arg.Any<CancellationToken>()).Returns(notification);
        _sms.RedactMessageBodyAsync(Arg.Any<string>())
            .Returns<Task>(_ => throw new SmsProviderException("provider down"));

        await Assert.ThrowsAsync<SmsProviderException>(() => Sut.DeleteContentAsync(9));

        Assert.False(notification.ContentDisposed);
    }
}
