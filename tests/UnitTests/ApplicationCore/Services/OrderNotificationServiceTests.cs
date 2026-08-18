using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class OrderNotificationServiceTests
{
    private readonly IRepository<Order> _orders = Substitute.For<IRepository<Order>>();
    private readonly IRepository<CatalogItem> _items = Substitute.For<IRepository<CatalogItem>>();
    private readonly IRepository<ContactNumber> _contacts = Substitute.For<IRepository<ContactNumber>>();
    private readonly IRepository<OrderNotification> _notifications = Substitute.For<IRepository<OrderNotification>>();
    private readonly ISmsGateway _gateway = Substitute.For<ISmsGateway>();
    private readonly IUriComposer _uriComposer = Substitute.For<IUriComposer>();
    private readonly IAppLogger<OrderNotificationService> _logger = Substitute.For<IAppLogger<OrderNotificationService>>();

    private OrderNotificationService CreateService() =>
        new(_orders, _items, _contacts, _notifications, _gateway, _uriComposer, _logger);

    private const string Buyer = "shopper@example.com";

    public OrderNotificationServiceTests()
    {
        _uriComposer.ComposePicUri(Arg.Any<string>()).Returns("pic.png");
        _orders.AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(callInfo.Arg<Order>()));
        _notifications.AddAsync(Arg.Any<OrderNotification>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(callInfo.Arg<OrderNotification>()));
        // The real gateway always returns a result or throws; give a benign default so unrelated
        // sends in a test don't return a null result.
        _gateway.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SmsDispatchResult("SMdefault", "queued", null, null));
        _gateway.ScheduleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<System.DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new SmsDispatchResult("SMscheduleddefault", "scheduled", null, null));
    }

    private static CatalogItem CatalogItemWithId(int id)
    {
        var item = new CatalogItem(1, 1, "desc", "name", 9.99m, "pic.png");
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!
            .GetSetMethod(nonPublic: true)!.Invoke(item, new object[] { id });
        return item;
    }

    private void CatalogHas(CatalogItem item) =>
        _items.ListAsync(Arg.Any<ISpecification<CatalogItem>>(), Arg.Any<CancellationToken>())
            .Returns(new List<CatalogItem> { item });

    private void BuyerHasOneNumber() =>
        _contacts.ListAsync(Arg.Any<ISpecification<ContactNumber>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber> { new(Buyer, "+14165551234") });

    private void BuyerHasNoNumbers() =>
        _contacts.ListAsync(Arg.Any<ISpecification<ContactNumber>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber>());

    [Fact]
    public async Task PlaceOrder_still_succeeds_when_the_message_cannot_be_sent()
    {
        CatalogHas(CatalogItemWithId(1));
        BuyerHasOneNumber();
        _gateway.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new SmsGatewayException("provider down"));

        OrderNotification? recorded = null;
        _notifications.AddAsync(Arg.Do<OrderNotification>(n => recorded = n), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(callInfo.Arg<OrderNotification>()));

        var service = CreateService();
        var order = await service.PlaceOrderAsync(Buyer, new List<OrderLineSelection> { new(1, 2) }, CancellationToken.None);

        Assert.NotNull(order);
        await _orders.Received().AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
        // A failed send is recorded, never thrown.
        Assert.NotNull(recorded);
        Assert.Null(recorded!.ProviderMessageSid);
        Assert.Equal("send_failed", recorded.Status);
    }

    [Fact]
    public async Task PlaceOrder_does_not_message_a_shopper_with_no_number()
    {
        CatalogHas(CatalogItemWithId(1));
        BuyerHasNoNumbers();

        var service = CreateService();
        await service.PlaceOrderAsync(Buyer, new List<OrderLineSelection> { new(1, 1) }, CancellationToken.None);

        await _gateway.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _notifications.DidNotReceive().AddAsync(Arg.Any<OrderNotification>(), Arg.Any<CancellationToken>());
        await _orders.Received().AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resend_under_a_repeated_key_sends_nothing_and_returns_the_first_result()
    {
        var existing = new OrderNotification(5, Buyer, NotificationKind.OrderPlaced, "+14165551234", "hi", false);
        existing.AttachIdempotencyKey("key-1");
        _notifications.FirstOrDefaultAsync(Arg.Any<ISpecification<OrderNotification>>(), Arg.Any<CancellationToken>())
            .Returns(existing);

        var service = CreateService();
        var result = await service.ResendAsync(99, "key-1", CancellationToken.None);

        Assert.Same(existing, result);
        await _gateway.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _notifications.DidNotReceive().AddAsync(Arg.Any<OrderNotification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resend_under_a_fresh_key_sends_a_new_message()
    {
        _notifications.FirstOrDefaultAsync(Arg.Any<ISpecification<OrderNotification>>(), Arg.Any<CancellationToken>())
            .Returns((OrderNotification?)null);
        var original = new OrderNotification(5, Buyer, NotificationKind.OrderDispatched, "+14165551234", "on its way", false);
        _notifications.GetByIdAsync(99, Arg.Any<CancellationToken>()).Returns(original);
        _gateway.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SmsDispatchResult("SMnew", "queued", null, null));

        var service = CreateService();
        var result = await service.ResendAsync(99, "fresh-key", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("SMnew", result!.ProviderMessageSid);
        Assert.Equal("fresh-key", result.IdempotencyKey);
        await _gateway.Received(1).SendAsync("+14165551234", "on its way", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancel_calls_off_a_scheduled_follow_up_at_the_provider()
    {
        var order = new Order(Buyer, new Address("s", "c", "st", "co", "00000"), new List<OrderItem>());
        _orders.GetByIdAsync(7, Arg.Any<CancellationToken>()).Returns(order);

        var followUp = new OrderNotification(7, Buyer, NotificationKind.DeliveryFeedback, "+14165551234", "feedback?", isScheduled: true);
        followUp.RecordProviderResult("SMscheduled", "scheduled", null, null);
        _notifications.ListAsync(Arg.Any<ISpecification<OrderNotification>>(), Arg.Any<CancellationToken>())
            .Returns(new List<OrderNotification> { followUp });
        _gateway.CancelScheduledAsync("SMscheduled", Arg.Any<CancellationToken>())
            .Returns(new SmsDispatchResult("SMscheduled", "canceled", null, null));
        BuyerHasOneNumber();

        var service = CreateService();
        await service.CancelOrderAsync(7, CancellationToken.None);

        await _gateway.Received(1).CancelScheduledAsync("SMscheduled", Arg.Any<CancellationToken>());
    }
}
