using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
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
    private const string BuyerId = "demouser@microsoft.com";
    private const string FromNumber = "+15550000000";

    private readonly IRepository<Order> _orders = Substitute.For<IRepository<Order>>();
    private readonly IRepository<CatalogItem> _catalogItems = Substitute.For<IRepository<CatalogItem>>();
    private readonly IRepository<ContactNumber> _contactNumbers = Substitute.For<IRepository<ContactNumber>>();
    private readonly IRepository<OrderNotification> _notifications = Substitute.For<IRepository<OrderNotification>>();
    private readonly ISmsNotificationGateway _gateway = Substitute.For<ISmsNotificationGateway>();
    private readonly IUriComposer _uriComposer = Substitute.For<IUriComposer>();
    private readonly IAppLogger<OrderNotificationService> _logger = Substitute.For<IAppLogger<OrderNotificationService>>();

    private static CatalogItem CatalogItemWithId(int id)
    {
        var item = new CatalogItem(1, 1, "d", "name", 10m, "p.png");
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.GetSetMethod(nonPublic: true)!.Invoke(item, new object[] { id });
        return item;
    }

    private OrderNotificationService CreateService()
    {
        _gateway.SendingNumber.Returns(FromNumber);
        _uriComposer.ComposePicUri(Arg.Any<string>()).Returns("pic");
        _notifications.AddAsync(Arg.Any<OrderNotification>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<OrderNotification>());
        _orders.AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Order>());
        return new OrderNotificationService(_orders, _catalogItems, _contactNumbers, _notifications, _gateway, _uriComposer, _logger);
    }

    [Fact]
    public async Task PlaceOrder_WhenSendFails_StillPlacesOrderAndRecordsFailedNotification()
    {
        var service = CreateService();
        _catalogItems.ListAsync(Arg.Any<CatalogItemsSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<CatalogItem> { CatalogItemWithId(5) });
        _contactNumbers.ListAsync(Arg.Any<ContactNumbersByBuyerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber> { new(BuyerId, "+15551112222") });
        _gateway.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new SmsGatewayException("provider down"));

        var result = await service.PlaceOrderAsync(BuyerId, new List<OrderLineRequest> { new(5, 1) });

        // The order is still placed even though the message could not be sent.
        Assert.Equal(PlaceOrderStatus.Placed, result.Status);
        await _orders.Received(1).AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
        // A failed-send record survives so an operator can resend it.
        await _notifications.Received().AddAsync(
            Arg.Is<OrderNotification>(n => n.DeliveryStatus == NotificationDeliveryStatus.SendFailed && n.ProviderMessageSid == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlaceOrder_WithNoContactNumbers_PlacesOrderAndSendsNothing()
    {
        var service = CreateService();
        _catalogItems.ListAsync(Arg.Any<CatalogItemsSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<CatalogItem> { CatalogItemWithId(5) });
        _contactNumbers.ListAsync(Arg.Any<ContactNumbersByBuyerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber>());

        var result = await service.PlaceOrderAsync(BuyerId, new List<OrderLineRequest> { new(5, 1) });

        Assert.Equal(PlaceOrderStatus.Placed, result.Status);
        await _gateway.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlaceOrder_WithUnknownCatalogItem_IsInvalidAndPlacesNothing()
    {
        var service = CreateService();
        _catalogItems.ListAsync(Arg.Any<CatalogItemsSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<CatalogItem>()); // requested id not found

        var result = await service.PlaceOrderAsync(BuyerId, new List<OrderLineRequest> { new(42, 1) });

        Assert.Equal(PlaceOrderStatus.InvalidRequest, result.Status);
        await _orders.DidNotReceive().AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resend_UnderSameKey_ReturnsExistingAndDoesNotSendAgain()
    {
        var service = CreateService();
        var existing = new OrderNotification(1, BuyerId, "+15551112222", FromNumber, NotificationKind.OrderPlaced, "hi");
        existing.TagAsResend(7, "key-1");
        _notifications.FirstOrDefaultAsync(Arg.Any<OrderNotificationByResendKeySpecification>(), Arg.Any<CancellationToken>())
            .Returns(existing);

        var result = await service.ResendAsync(7, "key-1");

        Assert.Equal(ResendStatus.Resent, result.Status);
        Assert.Same(existing, result.Notification);
        await _gateway.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resend_WithFreshKey_SendsAndTagsNewNotification()
    {
        var service = CreateService();
        _notifications.FirstOrDefaultAsync(Arg.Any<OrderNotificationByResendKeySpecification>(), Arg.Any<CancellationToken>())
            .Returns((OrderNotification?)null);
        var original = new OrderNotification(1, BuyerId, "+15551112222", FromNumber, NotificationKind.OrderPlaced, "original body");
        _notifications.GetByIdAsync(7, Arg.Any<CancellationToken>()).Returns(original);
        _gateway.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SmsDispatchResult("SMnew", "queued", FromNumber));

        var result = await service.ResendAsync(7, "key-2");

        Assert.Equal(ResendStatus.Resent, result.Status);
        Assert.Equal("key-2", result.Notification!.ResendIdempotencyKey);
        Assert.Equal("SMnew", result.Notification.ProviderMessageSid);
        await _gateway.Received(1).SendAsync("+15551112222", "original body", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resend_WhenContentDisposed_CannotResend()
    {
        var service = CreateService();
        _notifications.FirstOrDefaultAsync(Arg.Any<OrderNotificationByResendKeySpecification>(), Arg.Any<CancellationToken>())
            .Returns((OrderNotification?)null);
        var original = new OrderNotification(1, BuyerId, "+15551112222", FromNumber, NotificationKind.OrderPlaced, "body");
        original.DisposeContent();
        _notifications.GetByIdAsync(7, Arg.Any<CancellationToken>()).Returns(original);

        var result = await service.ResendAsync(7, "key-3");

        Assert.Equal(ResendStatus.NothingResendable, result.Status);
        await _gateway.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancel_CallsOffTheScheduledFollowUpBeforeItGoesOut()
    {
        var service = CreateService();
        var order = new Order(BuyerId, new Address("s", "c", "st", "co", "z"),
            new List<OrderItem> { new(new CatalogItemOrdered(1, "n", "p"), 10m, 1) });
        _orders.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(order);

        var followUp = new OrderNotification(5, BuyerId, "+15551112222", "messaging-service",
            NotificationKind.DeliveryFollowUp, "how did it go?", isScheduled: true, scheduledFor: DateTimeOffset.UtcNow.AddDays(3));
        followUp.MarkAccepted("SMsched", "scheduled");
        _notifications.ListAsync(Arg.Any<OrderNotificationsByOrderSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<OrderNotification> { followUp });
        _contactNumbers.ListAsync(Arg.Any<ContactNumbersByBuyerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber>());

        var result = await service.CancelOrderAsync(5);

        Assert.Equal(OrderActionStatus.Done, result.Status);
        await _gateway.Received(1).CancelScheduledMessageAsync("SMsched", Arg.Any<CancellationToken>());
        Assert.Equal(NotificationDeliveryStatus.Canceled, followUp.DeliveryStatus);
    }

    [Fact]
    public async Task Dispatch_WhenOrderMissing_ReturnsNotFound()
    {
        var service = CreateService();
        _orders.GetByIdAsync(99, Arg.Any<CancellationToken>()).Returns((Order?)null);

        var result = await service.DispatchOrderAsync(99);

        Assert.Equal(OrderActionStatus.OrderNotFound, result.Status);
    }
}
