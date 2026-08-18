using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class OrderNotificationServiceTests
{
    private readonly IReadRepository<ContactNumber> _contactNumbers = Substitute.For<IReadRepository<ContactNumber>>();
    private readonly IRepository<Notification> _notifications = Substitute.For<IRepository<Notification>>();
    private readonly ISmsProvider _smsProvider = Substitute.For<ISmsProvider>();
    private readonly IAppLogger<OrderNotificationService> _logger = Substitute.For<IAppLogger<OrderNotificationService>>();

    private OrderNotificationService CreateService() =>
        new(_contactNumbers, _notifications, _smsProvider, _logger, new OrderNotificationOptions());

    private static Order NewOrder(string buyerId = "buyer@example.com") =>
        new(buyerId, new Address("1 St", "City", "ST", "Country", "00000"), new List<OrderItem>());

    private void HasContactNumbers(params string[] numbers)
    {
        var list = new List<ContactNumber>();
        foreach (var n in numbers)
        {
            list.Add(new ContactNumber("buyer@example.com", n));
        }
        _contactNumbers.ListAsync(Arg.Any<ContactNumbersByBuyerSpecification>(), Arg.Any<CancellationToken>()).Returns(list);
    }

    [Fact]
    public async Task PlacedMessageFailure_DoesNotThrow_AndRecordsFailedNotification()
    {
        HasContactNumbers("+15551234567");
        _smsProvider.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new SmsProviderException("provider down"));

        Notification? recorded = null;
        _notifications.AddAsync(Arg.Do<Notification>(n => recorded = n), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Notification>());

        // Must not throw — a messaging failure never fails the order operation.
        await CreateService().NotifyOrderPlacedAsync(NewOrder(), CancellationToken.None);

        Assert.NotNull(recorded);
        Assert.True(recorded!.SendFailed);
        Assert.Null(recorded.ProviderSid);
        Assert.Equal(NotificationType.OrderPlaced, recorded.Type);
    }

    [Fact]
    public async Task NoContactNumbers_SendsNothing_AndRecordsNothing()
    {
        HasContactNumbers(); // none on file

        await CreateService().NotifyOrderPlacedAsync(NewOrder(), CancellationToken.None);

        await _smsProvider.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _notifications.DidNotReceive().AddAsync(Arg.Any<Notification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Placed_Success_RecordsAcceptedNotificationWithSidAndStatus()
    {
        HasContactNumbers("+15551234567");
        _smsProvider.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SmsSendResult("SM123", "queued"));

        Notification? recorded = null;
        _notifications.AddAsync(Arg.Do<Notification>(n => recorded = n), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Notification>());

        await CreateService().NotifyOrderPlacedAsync(NewOrder(), CancellationToken.None);

        Assert.NotNull(recorded);
        Assert.False(recorded!.SendFailed);
        Assert.Equal("SM123", recorded.ProviderSid);
        Assert.Equal("queued", recorded.DeliveryStatus);
    }

    [Fact]
    public async Task Cancelled_CallsOffPendingScheduledFollowUps()
    {
        HasContactNumbers("+15551234567");

        // A scheduled follow-up that is still eligible to be cancelled.
        var followUp = Notification.CreateScheduled(0, "buyer@example.com", "+15551234567",
            NotificationType.DeliveryFeedback, "How did delivery go?", DateTimeOffset.UtcNow.AddDays(3));
        followUp.MarkAccepted("SMscheduled", "scheduled");

        _notifications.ListAsync(Arg.Any<ScheduledFollowUpsByOrderSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<Notification> { followUp });
        _smsProvider.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SmsSendResult("SMcancelmsg", "queued"));

        await CreateService().NotifyOrderCancelledAsync(NewOrder(), CancellationToken.None);

        // The scheduled follow-up was cancelled at the provider and its record marked canceled.
        await _smsProvider.Received(1).CancelScheduledAsync("SMscheduled", Arg.Any<CancellationToken>());
        Assert.Equal("canceled", followUp.DeliveryStatus);
        await _notifications.Received().UpdateAsync(followUp, Arg.Any<CancellationToken>());
    }
}
