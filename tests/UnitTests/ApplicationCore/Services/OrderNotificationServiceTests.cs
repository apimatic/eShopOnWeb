using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.UnitTests.Builders;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class OrderNotificationServiceTests
{
    [Fact]
    public async Task OrderPlacementSucceedsWhenTheProviderRejectsTheMessage()
    {
        var notifications = Substitute.For<IRepository<OrderNotification>>();
        notifications.AddAsync(Arg.Any<OrderNotification>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<OrderNotification>());

        var contacts = Substitute.For<IContactNumberService>();
        contacts.GetLatestForBuyerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ContactNumber("buyer-1", "+15555550100", "(555) 555-0100", "US"));

        var messaging = Substitute.For<ITwilioMessagingClient>();
        messaging.SendAsync(Arg.Any<SendProviderMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns<ProviderMessage>(_ => throw new InvalidOperationException("provider unavailable"));

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.Parse("2026-08-27T12:00:00Z"));

        var logger = Substitute.For<IAppLogger<OrderNotificationService>>();
        var service = new OrderNotificationService(notifications, contacts, messaging, clock, logger);

        var order = new OrderBuilder().WithDefaultValues();
        await service.NotifyOrderPlacedAsync(order);

        await notifications.Received(1).AddAsync(
            Arg.Is<OrderNotification>(n => n.Kind == NotificationKind.OrderPlaced && n.ProviderStatus == "failed"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShopperWithNoNumberOnFileIsNotMessaged()
    {
        var notifications = Substitute.For<IRepository<OrderNotification>>();
        var contacts = Substitute.For<IContactNumberService>();
        contacts.GetLatestForBuyerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((ContactNumber?)null);

        var messaging = Substitute.For<ITwilioMessagingClient>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var logger = Substitute.For<IAppLogger<OrderNotificationService>>();
        var service = new OrderNotificationService(notifications, contacts, messaging, clock, logger);

        await service.NotifyOrderPlacedAsync(new OrderBuilder().WithDefaultValues());

        await messaging.DidNotReceive().SendAsync(Arg.Any<SendProviderMessageRequest>(), Arg.Any<CancellationToken>());
        await notifications.DidNotReceive().AddAsync(Arg.Any<OrderNotification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResendWithTheSameIdempotencyKeyDoesNotSendAgain()
    {
        var existing = new OrderNotification(1, "buyer-1", NotificationKind.OrderPlaced, "body", idempotencyKey: "key-1", sourceNotificationId: 9);

        var notifications = Substitute.For<IRepository<OrderNotification>>();
        notifications.FirstOrDefaultAsync(Arg.Any<OrderNotificationByResendKeySpecification>(), Arg.Any<CancellationToken>())
            .Returns(existing);

        var contacts = Substitute.For<IContactNumberService>();
        var messaging = Substitute.For<ITwilioMessagingClient>();
        var clock = Substitute.For<IClock>();
        var logger = Substitute.For<IAppLogger<OrderNotificationService>>();
        var service = new OrderNotificationService(notifications, contacts, messaging, clock, logger);

        var result = await service.ResendAsync(9, "key-1");

        Assert.Same(existing, result);
        await messaging.DidNotReceive().SendAsync(Arg.Any<SendProviderMessageRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelCallsOffAScheduledFollowUp()
    {
        var followUp = new OrderNotification(1, "buyer-1", NotificationKind.DeliveryFollowUp, "how did it go", DateTimeOffset.UtcNow.AddDays(3));
        followUp.RecordAccepted("SM12345678901234567890123456789012", "scheduled");

        var notifications = Substitute.For<IRepository<OrderNotification>>();
        notifications.ListAsync(Arg.Any<ScheduledFollowUpByOrderIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<OrderNotification> { followUp });
        notifications.AddAsync(Arg.Any<OrderNotification>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<OrderNotification>());

        var contacts = Substitute.For<IContactNumberService>();
        contacts.GetLatestForBuyerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((ContactNumber?)null);

        var messaging = Substitute.For<ITwilioMessagingClient>();
        messaging.CancelAsync("SM12345678901234567890123456789012", Arg.Any<CancellationToken>())
            .Returns(new ProviderMessage { Sid = "SM12345678901234567890123456789012", Status = "canceled" });

        var clock = Substitute.For<IClock>();
        var logger = Substitute.For<IAppLogger<OrderNotificationService>>();
        var service = new OrderNotificationService(notifications, contacts, messaging, clock, logger);

        await service.NotifyOrderCancelledAsync(new OrderBuilder().WithDefaultValues());

        await messaging.Received(1).CancelAsync("SM12345678901234567890123456789012", Arg.Any<CancellationToken>());
        Assert.Equal("canceled", followUp.ProviderStatus);
    }
}
