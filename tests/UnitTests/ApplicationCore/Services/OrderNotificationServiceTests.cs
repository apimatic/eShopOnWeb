using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class OrderNotificationServiceTests
{
    private readonly IRepository<Order> _orders = Substitute.For<IRepository<Order>>();
    private readonly IReadRepository<CatalogItem> _catalogItems = Substitute.For<IReadRepository<CatalogItem>>();
    private readonly IRepository<Notification> _notifications = Substitute.For<IRepository<Notification>>();
    private readonly IReadRepository<ContactNumber> _contactNumbers = Substitute.For<IReadRepository<ContactNumber>>();
    private readonly ITwilioMessagingGateway _gateway = Substitute.For<ITwilioMessagingGateway>();
    private readonly IUriComposer _uriComposer = Substitute.For<IUriComposer>();
    private readonly IAppLogger<OrderNotificationService> _logger = Substitute.For<IAppLogger<OrderNotificationService>>();

    private OrderNotificationService CreateService() =>
        new(_orders, _catalogItems, _notifications, _contactNumbers, _gateway, _uriComposer, _logger);

    private static Order NewSubmittedOrder() =>
        new("buyer@test.com",
            new Address("1 St", "City", "State", "Country", "00000"),
            new List<OrderItem> { new(new CatalogItemOrdered(1, "Item", "uri"), 10m, 1) });

    private static ContactNumber ANumber() => new("buyer@test.com", "+15551234567");

    [Fact]
    public async Task DispatchStillSucceedsWhenTheProviderCallFails()
    {
        _orders.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(NewSubmittedOrder());
        _contactNumbers.ListAsync(Arg.Any<ContactNumbersByBuyerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber> { ANumber() });
        // Every provider call fails.
        _gateway.SendSmsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ProviderMessageState>(new Exception("provider down")));
        _gateway.ScheduleSmsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ProviderMessageState>(new Exception("provider down")));

        var result = await CreateService().DispatchAsync(42, CancellationToken.None);

        // The underlying operation succeeds regardless of the messaging failure...
        Assert.Equal(OrderActionStatus.Success, result.Status);
        // ...the state change was committed...
        await _orders.Received().UpdateAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
        // ...and the failed sends were still recorded as notifications.
        await _notifications.Received().AddAsync(Arg.Any<Notification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchReturnsNotFoundForUnknownOrder()
    {
        _orders.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns((Order?)null);

        var result = await CreateService().DispatchAsync(999, CancellationToken.None);

        Assert.Equal(OrderActionStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task CancelCallsOffAPendingFollowUp()
    {
        var followUp = new Notification(1, "buyer@test.com", NotificationKind.DeliveryFollowUp, "+15551234567", "feedback?",
            isScheduled: true, scheduledSendAt: DateTimeOffset.UtcNow.AddDays(3));
        followUp.MarkScheduled("SM_followup", NotificationStatus.Scheduled);

        _orders.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(NewSubmittedOrder());
        _notifications.ListAsync(Arg.Any<PendingFollowUpsByOrderSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<Notification> { followUp });
        _contactNumbers.ListAsync(Arg.Any<ContactNumbersByBuyerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber> { ANumber() });
        _gateway.CancelScheduledMessageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ProviderMessageState("SM_followup", NotificationStatus.Canceled, null, null));
        _gateway.SendSmsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ProviderMessageState("SM_cancel", NotificationStatus.Queued, null, null));

        var result = await CreateService().CancelAsync(7, CancellationToken.None);

        Assert.Equal(OrderActionStatus.Success, result.Status);
        await _gateway.Received(1).CancelScheduledMessageAsync("SM_followup", Arg.Any<CancellationToken>());
        Assert.Equal(NotificationStatus.Canceled, followUp.Status);
    }

    [Fact]
    public async Task ResendUnderAKnownKeyDoesNotSendAgain()
    {
        var existing = new Notification(1, "buyer@test.com", NotificationKind.OrderPlaced, "+15551234567", "hi",
            idempotencyKey: "key-1");
        _notifications.FirstOrDefaultAsync(Arg.Any<NotificationByIdempotencyKeySpecification>(), Arg.Any<CancellationToken>())
            .Returns(existing);

        var result = await CreateService().ResendAsync(5, "key-1", CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.Deduplicated);
        await _gateway.DidNotReceive().SendSmsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResendUnderAFreshKeySendsANewMessage()
    {
        var original = new Notification(1, "buyer@test.com", NotificationKind.OrderPlaced, "+15551234567", "hi");
        _notifications.FirstOrDefaultAsync(Arg.Any<NotificationByIdempotencyKeySpecification>(), Arg.Any<CancellationToken>())
            .Returns((Notification?)null);
        _notifications.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(original);
        _gateway.SendSmsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ProviderMessageState("SM_new", NotificationStatus.Queued, null, null));

        var result = await CreateService().ResendAsync(5, "fresh-key", CancellationToken.None);

        Assert.NotNull(result);
        Assert.False(result!.Deduplicated);
        await _gateway.Received(1).SendSmsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OrderNotificationsAreScopedToTheOwningShopper()
    {
        // The order is not the caller's (spec returns nothing).
        _orders.FirstOrDefaultAsync(Arg.Any<OrderByIdForBuyerSpecification>(), Arg.Any<CancellationToken>())
            .Returns((Order?)null);

        var result = await CreateService().GetOrderNotificationsAsync(1, "someone-else@test.com", CancellationToken.None);

        Assert.Null(result);
    }
}
