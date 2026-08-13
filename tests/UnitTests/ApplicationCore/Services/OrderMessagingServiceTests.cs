using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class OrderMessagingServiceTests
{
    private readonly IRepository<Order> _orders = Substitute.For<IRepository<Order>>();
    private readonly IRepository<CatalogItem> _catalogItems = Substitute.For<IRepository<CatalogItem>>();
    private readonly IRepository<ContactNumber> _contactNumbers = Substitute.For<IRepository<ContactNumber>>();
    private readonly IRepository<OrderNotification> _notifications = Substitute.For<IRepository<OrderNotification>>();
    private readonly INotificationGateway _gateway = Substitute.For<INotificationGateway>();
    private readonly INotificationSettings _settings = Substitute.For<INotificationSettings>();
    private readonly IUriComposer _uriComposer = Substitute.For<IUriComposer>();
    private readonly IAppLogger<OrderMessagingService> _logger = Substitute.For<IAppLogger<OrderMessagingService>>();

    public OrderMessagingServiceTests()
    {
        _settings.FollowUpDelayDays.Returns(3);
        _uriComposer.ComposePicUri(Arg.Any<string>()).Returns("http://pic/x.png");
    }

    private OrderMessagingService CreateService() => new(
        _orders, _catalogItems, _contactNumbers, _notifications, _gateway, _settings, _uriComposer, _logger);

    private static Order NewOrder() =>
        new("owner-1", new Address("s", "c", "st", "co", "z"),
            new List<OrderItem> { new(new CatalogItemOrdered(1, "Item", "pic"), 10m, 1) });

    [Fact]
    public async Task SendFailureDoesNotFailDispatch()
    {
        var order = NewOrder();
        _orders.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(order);
        _contactNumbers.ListAsync(Arg.Any<ISpecification<ContactNumber>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber> { new("owner-1", "+14155550000") });
        // The provider throws for both the immediate send and the scheduled follow-up.
        _gateway.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("boom"));
        _gateway.ScheduleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("boom"));

        var service = CreateService();

        // Must not throw: a messaging failure never fails the underlying operation.
        var result = await service.DispatchAsync(1);

        Assert.Equal(OrderStatus.Dispatched, result.Status);
        await _orders.Received(1).UpdateAsync(order, Arg.Any<CancellationToken>());
        // A notification record is still written (with a send-error state) for each attempt.
        await _notifications.Received().AddAsync(Arg.Any<OrderNotification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BuyerWithNoNumberIsNotMessagedButOrderStillPlaced()
    {
        _catalogItems.ListAsync(Arg.Any<ISpecification<CatalogItem>>(), Arg.Any<CancellationToken>())
            .Returns(new List<CatalogItem> { MakeCatalogItem() });
        _orders.AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<Order>());
        _contactNumbers.ListAsync(Arg.Any<ISpecification<ContactNumber>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber>()); // no numbers on file

        var service = CreateService();
        var order = await service.PlaceOrderAsync("owner-1",
            new[] { new OrderLine(1, 2) }, new Address("s", "c", "st", "co", "z"));

        Assert.NotNull(order);
        await _orders.Received(1).AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
        await _gateway.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _notifications.DidNotReceive().AddAsync(Arg.Any<OrderNotification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResendUnderSameKeyDoesNotSendAgain()
    {
        var existing = new OrderNotification(1, "owner-1", "+14155550000", NotificationType.OrderPlaced, "hi");
        existing.MarkAsResendOf(7, "dup-key");
        _notifications.FirstOrDefaultAsync(Arg.Any<ISpecification<OrderNotification>>(), Arg.Any<CancellationToken>())
            .Returns(existing);

        var service = CreateService();
        var result = await service.ResendAsync(7, "dup-key");

        Assert.Same(existing, result);
        await _gateway.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _notifications.DidNotReceive().AddAsync(Arg.Any<OrderNotification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResendUnderFreshKeySendsAndPersists()
    {
        var original = new OrderNotification(1, "owner-1", "+14155550000", NotificationType.OrderPlaced, "hi");
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.SetValue(original, 3);
        _notifications.FirstOrDefaultAsync(Arg.Any<ISpecification<OrderNotification>>(), Arg.Any<CancellationToken>())
            .Returns((OrderNotification?)null);
        _notifications.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(original);
        _gateway.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SentMessageResult("SMnew", NotificationStatus.Queued, null, null, null));

        var service = CreateService();
        var result = await service.ResendAsync(3, "fresh-key");

        Assert.Equal("SMnew", result.ProviderMessageSid);
        Assert.Equal(3, result.ResendOfNotificationId);
        Assert.Equal("fresh-key", result.IdempotencyKey);
        await _gateway.Received(1).SendAsync("+14155550000", "hi", Arg.Any<CancellationToken>());
        await _notifications.Received(1).AddAsync(Arg.Any<OrderNotification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelCallsOffPendingFollowUpsBeforeNotifying()
    {
        var order = NewOrder();
        _orders.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(order);

        var followUp = new OrderNotification(1, "owner-1", "+14155550000", NotificationType.DeliveryFollowUp, "hi");
        followUp.RecordScheduled("SMsched", NotificationStatus.Scheduled, DateTimeOffset.UtcNow.AddDays(3));
        _notifications.ListAsync(Arg.Any<ISpecification<OrderNotification>>(), Arg.Any<CancellationToken>())
            .Returns(new List<OrderNotification> { followUp });
        _contactNumbers.ListAsync(Arg.Any<ISpecification<ContactNumber>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber>());

        var service = CreateService();
        var result = await service.CancelAsync(1);

        Assert.Equal(OrderStatus.Cancelled, result.Status);
        await _gateway.Received(1).CancelScheduledAsync("SMsched", Arg.Any<CancellationToken>());
        Assert.Equal(NotificationStatus.Canceled, followUp.Status);
    }

    [Fact]
    public async Task DisposeContentRedactsAtProviderAndLocally()
    {
        var notification = new OrderNotification(1, "owner-1", "+14155550000", NotificationType.OrderPlaced, "secret text");
        notification.RecordSendResult("SMabc", NotificationStatus.Delivered, null, null, DateTimeOffset.UtcNow);
        _notifications.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(notification);

        var service = CreateService();
        await service.DisposeContentAsync(10);

        await _gateway.Received(1).RedactContentAsync("SMabc", Arg.Any<CancellationToken>());
        Assert.True(notification.ContentRedacted);
        Assert.Null(notification.Body);
        Assert.Equal(NotificationStatus.Delivered, notification.Status); // outcome survives
    }

    private static CatalogItem MakeCatalogItem()
    {
        var item = new CatalogItem(1, 1, "desc", "Item", 10m, "pic");
        // Id defaults to 0; the specification match in the service is by the ids we pass, so set via reflection.
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.SetValue(item, 1);
        return item;
    }
}
