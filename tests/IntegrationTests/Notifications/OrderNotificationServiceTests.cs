using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Data;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Notifications;

public class OrderNotificationServiceTests
{
    private const string Buyer = "shopper@example.com";
    private const string Number = "+14165551234";

    private readonly CatalogContext _context;
    private readonly ISmsGateway _gateway = Substitute.For<ISmsGateway>();
    private readonly IUriComposer _uriComposer = Substitute.For<IUriComposer>();
    private readonly IAppLogger<OrderNotificationService> _logger = Substitute.For<IAppLogger<OrderNotificationService>>();
    private readonly OrderNotificationService _service;

    private readonly EfRepository<Order> _orders;
    private readonly EfRepository<CatalogItem> _items;
    private readonly EfRepository<ContactNumber> _numbers;
    private readonly EfRepository<OrderNotification> _notifications;

    public OrderNotificationServiceTests()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase($"Notifications-{Guid.NewGuid()}")
            .Options;
        _context = new CatalogContext(options);
        _orders = new EfRepository<Order>(_context);
        _items = new EfRepository<CatalogItem>(_context);
        _numbers = new EfRepository<ContactNumber>(_context);
        _notifications = new EfRepository<OrderNotification>(_context);

        _uriComposer.ComposePicUri(Arg.Any<string>()).Returns(ci => ci.Arg<string>());
        _gateway.SendingNumber.Returns("+15550000000");
        _gateway.FetchDeliveryStateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SmsDeliveryState("delivered", null, null, null));

        _service = new OrderNotificationService(_orders, _items, _numbers, _notifications, _gateway, _uriComposer, _logger);
    }

    private async Task<int> SeedCatalogItemAsync()
    {
        var item = new CatalogItem(1, 1, "desc", "Test Widget", 9.99m, "pic.png");
        await _items.AddAsync(item);
        return item.Id;
    }

    private async Task RegisterNumberAsync(string owner = Buyer, string number = Number)
    {
        await _numbers.AddAsync(new ContactNumber(owner, number));
    }

    [Fact]
    public async Task PlaceOrder_WhenProviderUnreachable_StillPlacesOrder_AndRecordsNotSent()
    {
        var itemId = await SeedCatalogItemAsync();
        await RegisterNumberAsync();
        _gateway.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<SmsSendResult>(_ => throw new SmsGatewayException("provider down"));

        var order = await _service.PlaceOrderAsync(Buyer, new List<OrderLine> { new(itemId, 2) },
            new Address("1 St", "City", "ST", "Country", "00000"));

        Assert.NotNull(order); // a message that cannot be sent must never fail the order
        var notes = await _notifications.ListAsync();
        var note = Assert.Single(notes);
        Assert.True(note.SendFailed);
        Assert.Equal(NotificationKind.OrderPlaced, note.Kind);
    }

    [Fact]
    public async Task PlaceOrder_WithNoNumberOnFile_PlacesOrder_WithNoNotifications()
    {
        var itemId = await SeedCatalogItemAsync();

        var order = await _service.PlaceOrderAsync(Buyer, new List<OrderLine> { new(itemId, 1) },
            new Address("1 St", "City", "ST", "Country", "00000"));

        Assert.NotNull(order);
        Assert.Empty(await _notifications.ListAsync());
    }

    [Fact]
    public async Task PlaceOrder_WithUnknownCatalogItem_ReturnsNull()
    {
        var order = await _service.PlaceOrderAsync(Buyer, new List<OrderLine> { new(999999, 1) },
            new Address("1 St", "City", "ST", "Country", "00000"));

        Assert.Null(order);
        Assert.Empty(await _orders.ListAsync());
    }

    [Fact]
    public async Task Resend_UnderSameKey_SendsOnce_FreshKeySendsAgain()
    {
        await RegisterNumberAsync();
        var original = new OrderNotification(1, Buyer, NotificationKind.OrderPlaced, Number, "hi");
        original.RecordAccepted("SMoriginal", "undelivered", 30006, "unreachable");
        await _notifications.AddAsync(original);

        _gateway.SendAsync(Number, "hi", Arg.Any<CancellationToken>())
            .Returns(new SmsSendResult("SMresent", "queued", null, null));

        var first = await _service.ResendAsync(original.Id, "key-1");
        var replay = await _service.ResendAsync(original.Id, "key-1");
        var fresh = await _service.ResendAsync(original.Id, "key-2");

        Assert.Equal(ResendOutcome.Sent, first.Outcome);
        Assert.Equal(ResendOutcome.AlreadyProcessed, replay.Outcome);
        Assert.Equal(first.Notification!.Id, replay.Notification!.Id); // same message, not a new one
        Assert.Equal(ResendOutcome.Sent, fresh.Outcome);

        // Two genuine sends (key-1, key-2); the replay sent nothing.
        await _gateway.Received(2).SendAsync(Number, "hi", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resend_AfterDestinationRemoved_IsRefused()
    {
        // Number is NOT registered.
        var original = new OrderNotification(1, Buyer, NotificationKind.OrderPlaced, Number, "hi");
        original.RecordAccepted("SMoriginal", "undelivered", null, null);
        await _notifications.AddAsync(original);

        var result = await _service.ResendAsync(original.Id, "key-1");

        Assert.Equal(ResendOutcome.DestinationRemoved, result.Outcome);
        await _gateway.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancel_CallsOffPendingFollowUp_SoItCanNeverReachTheShopper()
    {
        var order = new Order(Buyer, new Address("1 St", "City", "ST", "Country", "00000"),
            new List<OrderItem> { new(new CatalogItemOrdered(1, "Widget", "pic.png"), 1m, 1) });
        await _orders.AddAsync(order);

        var followUp = new OrderNotification(order.Id, Buyer, NotificationKind.DeliveryFollowUp, Number, "how did it go?");
        followUp.RecordAccepted("SMscheduled", "scheduled", null, null, DateTimeOffset.UtcNow.AddDays(3));
        await _notifications.AddAsync(followUp);

        var result = await _service.CancelAsync(order.Id);

        Assert.Equal(OrderTransitionOutcome.Success, result.Outcome);
        await _gateway.Received(1).CancelScheduledAsync("SMscheduled", Arg.Any<CancellationToken>());

        var refreshed = await _notifications.GetByIdAsync(followUp.Id);
        Assert.Equal("canceled", refreshed!.ProviderStatus);
    }

    [Fact]
    public async Task GetOrderNotifications_ForAnotherShoppersOrder_ReturnsNull()
    {
        var order = new Order("other-shopper", new Address("1 St", "City", "ST", "Country", "00000"),
            new List<OrderItem> { new(new CatalogItemOrdered(1, "Widget", "pic.png"), 1m, 1) });
        await _orders.AddAsync(order);

        var result = await _service.GetOrderNotificationsAsync(Buyer, order.Id);

        Assert.Null(result);
    }

    [Fact]
    public async Task DisposeContent_RedactsAtProvider_AndClearsBody_ButKeepsRecordAndStatus()
    {
        var note = new OrderNotification(1, Buyer, NotificationKind.OrderPlaced, Number, "secret text");
        note.RecordAccepted("SMx", "delivered", null, null);
        await _notifications.AddAsync(note);

        var disposed = await _service.DisposeContentAsync(note.Id);

        Assert.True(disposed);
        await _gateway.Received(1).RedactContentAsync("SMx", Arg.Any<CancellationToken>());
        var refreshed = await _notifications.GetByIdAsync(note.Id);
        Assert.Null(refreshed!.Body);
        Assert.True(refreshed.ContentRedacted);
        Assert.Equal("delivered", refreshed.ProviderStatus); // what became of it survives
    }
}
