using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Data;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Services;

public class OrderNotificationServiceTests
{
    private const string Owner = "shopper@example.com";
    private readonly CatalogContext _context;
    private readonly EfRepository<Order> _orders;
    private readonly EfRepository<CatalogItem> _items;
    private readonly EfRepository<ContactNumber> _numbers;
    private readonly EfRepository<OrderNotification> _notifications;
    private readonly ISmsGateway _gateway = Substitute.For<ISmsGateway>();
    private readonly IUriComposer _uriComposer = Substitute.For<IUriComposer>();
    private readonly OrderNotificationService _service;

    public OrderNotificationServiceTests()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase($"NotifTests-{Guid.NewGuid()}")
            .Options;
        _context = new CatalogContext(options);
        _orders = new EfRepository<Order>(_context);
        _items = new EfRepository<CatalogItem>(_context);
        _numbers = new EfRepository<ContactNumber>(_context);
        _notifications = new EfRepository<OrderNotification>(_context);

        _uriComposer.ComposePicUri(Arg.Any<string>()).Returns(ci => "http://example/img.png");
        _gateway.FromNumber.Returns("+15550000000");
        _gateway.GetMessageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((SmsSendResult?)null);

        _service = new OrderNotificationService(_orders, _items, _numbers, _notifications, _gateway, _uriComposer,
            Substitute.For<IAppLogger<OrderNotificationService>>());
    }

    private async Task<int> SeedItemAsync(decimal price = 10m)
    {
        var item = new CatalogItem(1, 1, "desc", "Widget", price, "img.png");
        await _items.AddAsync(item);
        return item.Id;
    }

    private async Task AddNumberAsync(string e164 = "+15551234567")
        => await _numbers.AddAsync(new ContactNumber(Owner, e164));

    [Fact]
    public async Task PlaceOrder_WithNumber_SendsAndRecordsAccepted()
    {
        var itemId = await SeedItemAsync();
        await AddNumberAsync();
        _gateway.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SmsSendResult("SM123", "queued", null));

        var orderId = await _service.PlaceOrderAsync(Owner, new[] { new OrderLine(itemId, 2) }, CancellationToken.None);

        Assert.True(orderId > 0);
        var notes = _context.OrderNotifications.Where(n => n.OrderId == orderId).ToList();
        var placed = Assert.Single(notes);
        Assert.Equal(NotificationKind.OrderPlaced, placed.Kind);
        Assert.Equal("SM123", placed.ProviderMessageSid);
        Assert.Equal("queued", placed.DeliveryStatus);
    }

    [Fact]
    public async Task PlaceOrder_NoNumberOnFile_IsNotMessaged_ButOrderStillPlaced()
    {
        var itemId = await SeedItemAsync();

        var orderId = await _service.PlaceOrderAsync(Owner, new[] { new OrderLine(itemId, 1) }, CancellationToken.None);

        await _gateway.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        var note = Assert.Single(_context.OrderNotifications.Where(n => n.OrderId == orderId).ToList());
        Assert.Null(note.ProviderMessageSid);
        Assert.Equal(OrderNotification.NotSentStatus, note.DeliveryStatus);
    }

    [Fact]
    public async Task PlaceOrder_SendThrows_DoesNotFailTheOrder()
    {
        var itemId = await SeedItemAsync();
        await AddNumberAsync();
        _gateway.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<SmsSendResult>(_ => throw new InvalidOperationException("provider down"));

        var orderId = await _service.PlaceOrderAsync(Owner, new[] { new OrderLine(itemId, 1) }, CancellationToken.None);

        Assert.True(orderId > 0); // order placed despite the send failure
        var note = Assert.Single(_context.OrderNotifications.Where(n => n.OrderId == orderId).ToList());
        Assert.Equal(OrderNotification.NotSentStatus, note.DeliveryStatus);
    }

    [Fact]
    public async Task Dispatch_QueuesFollowUp_AndCancel_CallsItOff()
    {
        var itemId = await SeedItemAsync();
        await AddNumberAsync();
        _gateway.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SmsSendResult("SMsent", "queued", null));
        _gateway.ScheduleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new SmsSendResult("SMsched", "scheduled", null));

        var orderId = await _service.PlaceOrderAsync(Owner, new[] { new OrderLine(itemId, 1) }, CancellationToken.None);
        await _service.DispatchAsync(orderId, CancellationToken.None);

        var followUp = _context.OrderNotifications.Single(n => n.OrderId == orderId && n.Kind == NotificationKind.DeliveryFeedback);
        Assert.True(followUp.IsScheduled);
        Assert.Equal("scheduled", followUp.DeliveryStatus);

        await _service.CancelAsync(orderId, CancellationToken.None);

        await _gateway.Received(1).CancelScheduledAsync("SMsched", Arg.Any<CancellationToken>());
        var followUpAfter = _context.OrderNotifications.Single(n => n.Id == followUp.Id);
        Assert.Equal("canceled", followUpAfter.DeliveryStatus);
    }

    [Fact]
    public async Task Resend_IsIdempotentOnKey_AndFreshKeySendsAgain()
    {
        var itemId = await SeedItemAsync();
        await AddNumberAsync();
        _gateway.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SmsSendResult("SMorig", "undelivered", 30034));

        var orderId = await _service.PlaceOrderAsync(Owner, new[] { new OrderLine(itemId, 1) }, CancellationToken.None);
        var source = _context.OrderNotifications.Single(n => n.OrderId == orderId);

        _gateway.ClearReceivedCalls();
        _gateway.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SmsSendResult("SMresend", "queued", null));

        var first = await _service.ResendAsync(source.Id, "key-1", CancellationToken.None);
        var repeat = await _service.ResendAsync(source.Id, "key-1", CancellationToken.None);
        var fresh = await _service.ResendAsync(source.Id, "key-2", CancellationToken.None);

        Assert.Equal(first, repeat);
        Assert.NotEqual(first, fresh);
        // Two distinct keys => exactly two sends, not three.
        await _gateway.Received(2).SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DisposeContent_RedactsAtProvider_AndClearsBody_ButKeepsOutcome()
    {
        var itemId = await SeedItemAsync();
        await AddNumberAsync();
        _gateway.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SmsSendResult("SMbody", "delivered", null));

        var orderId = await _service.PlaceOrderAsync(Owner, new[] { new OrderLine(itemId, 1) }, CancellationToken.None);
        var note = _context.OrderNotifications.Single(n => n.OrderId == orderId);

        var ok = await _service.DisposeContentAsync(note.Id, CancellationToken.None);

        Assert.True(ok);
        await _gateway.Received(1).RedactBodyAsync("SMbody", Arg.Any<CancellationToken>());
        var after = _context.OrderNotifications.Single(n => n.Id == note.Id);
        Assert.True(after.ContentDisposed);
        Assert.Null(after.Body);
        Assert.Equal("delivered", after.DeliveryStatus); // outcome survives
        Assert.Equal("SMbody", after.ProviderMessageSid); // fact survives
    }

    [Fact]
    public async Task Reconcile_ExcludesNeverSent_AndMatchesBySid()
    {
        var itemId = await SeedItemAsync();
        await AddNumberAsync();
        _gateway.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SmsSendResult("SMreal", "delivered", null));
        _gateway.ScheduleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new SmsSendResult("SMsched", "scheduled", null));

        var orderId = await _service.PlaceOrderAsync(Owner, new[] { new OrderLine(itemId, 1) }, CancellationToken.None);
        await _service.DispatchAsync(orderId, CancellationToken.None); // adds a scheduled (never-sent) follow-up

        var now = DateTimeOffset.UtcNow;
        _gateway.ListMessagesAsync("+15550000000", Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new List<ProviderMessage>
            {
                new("SMreal", "+15551234567", "+15550000000", "delivered", null, now),
                new("SMdispatch", "+15551234567", "+15550000000", "delivered", null, now), // provider knows, eShop's dispatch sid differs
            });

        var report = await _service.ReconcileAsync(now.AddMinutes(-10), now.AddMinutes(10), CancellationToken.None);

        // The placed + dispatched sends count as "sent"; the scheduled follow-up does not.
        Assert.DoesNotContain(report.OnlyInEShop, e => e.EShopStatus == "scheduled");
        Assert.Contains(report.Matched, m => m.ProviderMessageSid == "SMreal");
        Assert.Contains(report.OnlyAtProvider, m => m.ProviderMessageSid == "SMdispatch");
        Assert.Equal("+15550000000", report.FromNumber);
    }
}
