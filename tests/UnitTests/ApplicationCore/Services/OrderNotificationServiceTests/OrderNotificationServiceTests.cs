using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.OrderNotificationServiceTests;

public class OrderNotificationServiceTests
{
    private const string Owner = "shopper@example.com";
    private const string ToNumber = "+15551230000";

    private readonly ISmsGateway _gateway = Substitute.For<ISmsGateway>();
    private readonly IRepository<Order> _orders = Substitute.For<IRepository<Order>>();
    private readonly IRepository<CatalogItem> _items = Substitute.For<IRepository<CatalogItem>>();
    private readonly IRepository<ContactNumber> _numbers = Substitute.For<IRepository<ContactNumber>>();
    private readonly IRepository<OrderNotification> _notifications = Substitute.For<IRepository<OrderNotification>>();
    private readonly IUriComposer _uri = Substitute.For<IUriComposer>();
    private readonly IAppLogger<OrderNotificationService> _logger = Substitute.For<IAppLogger<OrderNotificationService>>();

    private OrderNotificationService CreateService()
    {
        _uri.ComposePicUri(Arg.Any<string>()).Returns("http://pic/img.png");
        return new OrderNotificationService(_gateway, _orders, _items, _numbers, _notifications, _uri, _logger);
    }

    private static void SetId(BaseEntity entity, int id)
        => typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.SetValue(entity, id);

    private static ContactNumber Number()
    {
        var c = new ContactNumber(Owner, ToNumber);
        SetId(c, 1);
        return c;
    }

    private static OrderNotification Accepted(int id, NotificationKind kind, string sid, bool followUp = false)
    {
        var n = followUp
            ? OrderNotification.ForScheduled(9, Owner, kind, ToNumber, "body", DateTimeOffset.UtcNow.AddDays(3))
            : OrderNotification.ForImmediate(9, Owner, kind, ToNumber, "body");
        n.RecordAccepted(sid, "+18286224987", "queued", DateTimeOffset.UtcNow);
        SetId(n, id);
        return n;
    }

    [Fact]
    public async Task PlaceOrder_WithNoContactNumber_PlacesOrderAndSendsNothing()
    {
        var catalogItem = new CatalogItem(1, 1, "desc", "name", 10m, "pic.png");
        SetId(catalogItem, 1);
        _items.ListAsync(Arg.Any<CatalogItemsSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<CatalogItem> { catalogItem });
        _numbers.ListAsync(Arg.Any<ContactNumbersByOwnerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber>()); // no number on file
        _orders.AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>())
            .Returns(ci => { SetId(ci.Arg<Order>(), 42); return ci.Arg<Order>(); });

        var service = CreateService();
        var order = await service.PlaceOrderAsync(Owner, new List<OrderLineRequest> { new(1, 2) }, default);

        Assert.Equal(42, order.Id);
        await _gateway.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _notifications.DidNotReceive().AddAsync(Arg.Any<OrderNotification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlaceOrder_WhenSendFails_StillPlacesOrderAndRecordsFailedNotification()
    {
        var catalogItem = new CatalogItem(1, 1, "desc", "name", 10m, "pic.png");
        SetId(catalogItem, 1);
        _items.ListAsync(Arg.Any<CatalogItemsSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<CatalogItem> { catalogItem });
        _numbers.ListAsync(Arg.Any<ContactNumbersByOwnerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber> { Number() });
        _orders.AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>())
            .Returns(ci => { SetId(ci.Arg<Order>(), 42); return ci.Arg<Order>(); });
        _gateway.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<SmsDispatchResult>(_ => throw new SmsGatewayException("provider down"));

        OrderNotification? recorded = null;
        await _notifications.AddAsync(Arg.Do<OrderNotification>(n => recorded = n), Arg.Any<CancellationToken>());

        var service = CreateService();

        // The send failing must not fail the order operation.
        var order = await service.PlaceOrderAsync(Owner, new List<OrderLineRequest> { new(1, 1) }, default);

        Assert.Equal(42, order.Id);
        Assert.NotNull(recorded);
        Assert.True(recorded!.SendFailed);
        Assert.Equal("send_failed", recorded.DeliveryStatus);
    }

    [Fact]
    public async Task Resend_UnderSameKey_IsReplay_AndDoesNotSendAgain()
    {
        var existing = Accepted(7, NotificationKind.OrderPlaced, "SMexisting");
        _notifications.FirstOrDefaultAsync(Arg.Any<OrderNotificationByResendKeySpecification>(), Arg.Any<CancellationToken>())
            .Returns(existing);

        var service = CreateService();
        var outcome = await service.ResendAsync(5, "dup-key", default);

        Assert.NotNull(outcome);
        Assert.True(outcome!.WasReplay);
        Assert.Equal(7, outcome.Notification.Id);
        await _gateway.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _notifications.DidNotReceive().AddAsync(Arg.Any<OrderNotification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resend_UnderFreshKey_Sends_AndRecordsNewNotification()
    {
        var original = Accepted(3, NotificationKind.OrderDispatched, "SMorig");
        _notifications.FirstOrDefaultAsync(Arg.Any<OrderNotificationByResendKeySpecification>(), Arg.Any<CancellationToken>())
            .Returns((OrderNotification?)null);
        _notifications.GetByIdAsync(3, Arg.Any<CancellationToken>()).Returns(original);
        _gateway.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SmsDispatchResult("SMnew", "+18286224987", "queued", DateTimeOffset.UtcNow));

        var service = CreateService();
        var outcome = await service.ResendAsync(3, "fresh-key", default);

        Assert.NotNull(outcome);
        Assert.False(outcome!.WasReplay);
        Assert.Equal("SMnew", outcome.Notification.ProviderSid);
        Assert.Equal("fresh-key", outcome.Notification.ResendIdempotencyKey);
        Assert.Equal(3, outcome.Notification.ResendOfNotificationId);
        await _gateway.Received(1).SendAsync(ToNumber, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _notifications.Received(1).AddAsync(Arg.Any<OrderNotification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resend_OfDisposedContent_IsRefused()
    {
        var disposed = Accepted(3, NotificationKind.OrderPlaced, "SMorig");
        disposed.DisposeContent();
        _notifications.FirstOrDefaultAsync(Arg.Any<OrderNotificationByResendKeySpecification>(), Arg.Any<CancellationToken>())
            .Returns((OrderNotification?)null);
        _notifications.GetByIdAsync(3, Arg.Any<CancellationToken>()).Returns(disposed);

        var service = CreateService();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResendAsync(3, "k", default));
        await _gateway.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancel_CallsOffScheduledFollowUp_AtProvider()
    {
        var order = new Order(Owner, new Address("s", "c", "st", "co", "z"), new List<OrderItem>());
        _orders.FirstOrDefaultAsync(Arg.Any<OrderWithItemsByIdSpec>(), Arg.Any<CancellationToken>()).Returns(order);
        _numbers.ListAsync(Arg.Any<ContactNumbersByOwnerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber>()); // keep the immediate cancel message out of this test
        var followUp = Accepted(11, NotificationKind.DeliveryFollowUp, "SMscheduled", followUp: true);
        _notifications.ListAsync(Arg.Any<ScheduledFollowUpForOrderSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<OrderNotification> { followUp });

        var service = CreateService();
        var result = await service.CancelOrderAsync(9, default);

        Assert.NotNull(result);
        await _gateway.Received(1).CancelScheduledAsync("SMscheduled", Arg.Any<CancellationToken>());
        Assert.Equal("canceled", followUp.DeliveryStatus);
        await _notifications.Received().UpdateAsync(followUp, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DisposeContent_RedactsAtProvider_AndClearsStoredBody()
    {
        var notification = Accepted(4, NotificationKind.OrderPlaced, "SMredact");
        _notifications.GetByIdAsync(4, Arg.Any<CancellationToken>()).Returns(notification);

        var service = CreateService();
        var result = await service.DisposeContentAsync(4, default);

        Assert.True(result);
        await _gateway.Received(1).RedactContentAsync("SMredact", Arg.Any<CancellationToken>());
        Assert.True(notification.ContentDisposed);
        Assert.Null(notification.Body);
        await _notifications.Received().UpdateAsync(notification, Arg.Any<CancellationToken>());
    }
}
