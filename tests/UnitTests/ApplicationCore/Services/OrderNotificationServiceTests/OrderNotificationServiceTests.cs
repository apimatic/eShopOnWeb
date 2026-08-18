using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.OrderNotificationServiceTests;

public class OrderNotificationServiceTests
{
    private const string BuyerId = "shopper@example.com";
    private const string Number = "+15195550123";

    private readonly IRepository<Order> _orderRepo = Substitute.For<IRepository<Order>>();
    private readonly IRepository<CatalogItem> _itemRepo = Substitute.For<IRepository<CatalogItem>>();
    private readonly IRepository<ContactNumber> _contactRepo = Substitute.For<IRepository<ContactNumber>>();
    private readonly IRepository<OrderNotification> _notifRepo = Substitute.For<IRepository<OrderNotification>>();
    private readonly ITwilioMessagingService _messaging = Substitute.For<ITwilioMessagingService>();
    private readonly IUriComposer _uriComposer = Substitute.For<IUriComposer>();
    private readonly IAppLogger<OrderNotificationService> _logger = Substitute.For<IAppLogger<OrderNotificationService>>();

    private OrderNotificationService CreateService() =>
        new(_orderRepo, _itemRepo, _contactRepo, _notifRepo, _messaging, _uriComposer, _logger);

    public OrderNotificationServiceTests()
    {
        // AddAsync echoes the entity back, as a real repository would after assigning an id.
        _notifRepo.AddAsync(Arg.Any<OrderNotification>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<OrderNotification>());
        _contactRepo.AddAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<ContactNumber>());
    }

    private static Order NewOrder() => new(BuyerId, new Address("s", "c", "st", "co", "z"), new List<OrderItem>());

    private void GivenOneContactNumber() =>
        _contactRepo.ListAsync(Arg.Any<ContactNumbersByBuyerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber> { new(BuyerId, Number) });

    [Fact]
    public async Task Dispatch_WhenSendFails_OrderStillDispatched_AndFailureRecorded()
    {
        GivenOneContactNumber();
        _messaging.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<MessageDispatchResult>>(_ => throw new SmsGatewayException("rejected", HttpStatusCode.BadRequest, 21211));
        _messaging.ScheduleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new MessageDispatchResult("SMsched", NotificationStatuses.Scheduled));
        var order = NewOrder();

        // Must not throw — a messaging failure never fails the order operation.
        await CreateService().DispatchOrderAsync(order);

        Assert.Equal(OrderStatus.Dispatched, order.Status);
        await _orderRepo.Received().UpdateAsync(order, Arg.Any<CancellationToken>());
        await _notifRepo.Received().AddAsync(
            Arg.Is<OrderNotification>(n => n.Type == NotificationType.OrderDispatched && n.Status == NotificationStatuses.SendFailed),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dispatch_QueuesDeliveryFollowUpWithProvider()
    {
        GivenOneContactNumber();
        _messaging.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MessageDispatchResult("SMsent", "queued"));
        _messaging.ScheduleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new MessageDispatchResult("SMsched", NotificationStatuses.Scheduled));

        await CreateService().DispatchOrderAsync(NewOrder());

        await _messaging.Received().ScheduleAsync(Number, Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await _notifRepo.Received().AddAsync(
            Arg.Is<OrderNotification>(n => n.Type == NotificationType.DeliveryFeedback && n.Status == NotificationStatuses.Scheduled && n.ProviderMessageSid == "SMsched"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancel_CallsOffQueuedFollowUp_SoItNeverReachesTheShopper()
    {
        var followUp = new OrderNotification(0, BuyerId, NotificationType.DeliveryFeedback, Number, "how did it go?", DateTimeOffset.UtcNow.AddDays(3));
        followUp.MarkAccepted("SMsched", NotificationStatuses.Scheduled);

        GivenOneContactNumber();
        _notifRepo.ListAsync(Arg.Any<ScheduledFollowUpsByOrderSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<OrderNotification> { followUp });
        _messaging.CancelScheduledAsync("SMsched", Arg.Any<CancellationToken>())
            .Returns(new MessageDispatchResult("SMsched", NotificationStatuses.Canceled));
        _messaging.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MessageDispatchResult("SMcancelmsg", "queued"));
        var order = NewOrder();

        await CreateService().CancelOrderAsync(order);

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        await _messaging.Received().CancelScheduledAsync("SMsched", Arg.Any<CancellationToken>());
        Assert.Equal(NotificationStatuses.Canceled, followUp.Status);
    }

    [Fact]
    public async Task Resend_UnderSameKey_DoesNotSendAgain()
    {
        var alreadySent = new OrderNotification(0, BuyerId, NotificationType.OrderPlaced, Number, "hi", idempotencyKey: "key-1");
        alreadySent.MarkAccepted("SMprior", "queued");
        _notifRepo.FirstOrDefaultAsync(Arg.Any<OrderNotificationByIdempotencyKeySpecification>(), Arg.Any<CancellationToken>())
            .Returns(alreadySent);

        var original = new OrderNotification(0, BuyerId, NotificationType.OrderPlaced, Number, "hi");
        original.MarkSendFailed(null, null);

        var result = await CreateService().ResendAsync(original, "key-1");

        Assert.Same(alreadySent, result);
        await _messaging.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _notifRepo.DidNotReceive().AddAsync(Arg.Any<OrderNotification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resend_UnderFreshKey_SendsAndRecordsWithKey()
    {
        _notifRepo.FirstOrDefaultAsync(Arg.Any<OrderNotificationByIdempotencyKeySpecification>(), Arg.Any<CancellationToken>())
            .Returns((OrderNotification?)null);
        _messaging.SendAsync(Number, "hi", Arg.Any<CancellationToken>())
            .Returns(new MessageDispatchResult("SMnew", "queued"));

        var original = new OrderNotification(0, BuyerId, NotificationType.OrderPlaced, Number, "hi");
        original.MarkAccepted("SMold", "queued");
        original.UpdateDeliveryStatus(NotificationStatuses.Undelivered, 30006, "unreachable");

        var result = await CreateService().ResendAsync(original, "key-2");

        await _messaging.Received().SendAsync(Number, "hi", Arg.Any<CancellationToken>());
        Assert.Equal("key-2", result.IdempotencyKey);
        Assert.Equal("SMnew", result.ProviderMessageSid);
        await _notifRepo.Received().AddAsync(Arg.Is<OrderNotification>(n => n.IdempotencyKey == "key-2"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resend_WhenContentDisposed_IsRejected()
    {
        _notifRepo.FirstOrDefaultAsync(Arg.Any<OrderNotificationByIdempotencyKeySpecification>(), Arg.Any<CancellationToken>())
            .Returns((OrderNotification?)null);
        var original = new OrderNotification(0, BuyerId, NotificationType.OrderPlaced, Number, "hi");
        original.MarkAccepted("SMold", "queued");
        original.UpdateDeliveryStatus(NotificationStatuses.Undelivered, null, null);
        original.RedactContent();

        await Assert.ThrowsAsync<OrderStatusException>(() => CreateService().ResendAsync(original, "key-3"));
        await _messaging.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Register_WhenNumberInvalid_IsRejected()
    {
        _messaging.ValidateNumberAsync("garbage", Arg.Any<CancellationToken>())
            .Returns(PhoneNumberValidationResult.Invalid("not a usable destination"));

        await Assert.ThrowsAsync<SmsGatewayException>(() => CreateService().RegisterContactNumberAsync(BuyerId, "garbage"));
        await _contactRepo.DidNotReceive().AddAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Register_WhenNumberValid_StoresProviderCanonicalForm()
    {
        _messaging.ValidateNumberAsync("519 555 0123", Arg.Any<CancellationToken>())
            .Returns(PhoneNumberValidationResult.Valid(Number));
        _contactRepo.ListAsync(Arg.Any<ContactNumbersByBuyerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber>());

        var result = await CreateService().RegisterContactNumberAsync(BuyerId, "519 555 0123");

        Assert.Equal(Number, result.PhoneNumber);
        await _contactRepo.Received().AddAsync(Arg.Is<ContactNumber>(c => c.PhoneNumber == Number), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshStatus_ForMessageWithNoSid_IsANoOp()
    {
        var notification = new OrderNotification(0, BuyerId, NotificationType.OrderPlaced, Number, "hi");
        notification.MarkSendFailed(null, null); // no SID

        await CreateService().RefreshStatusAsync(notification);

        await _messaging.DidNotReceive().FetchStatusAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
