using System;
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

namespace Microsoft.eShopWeb.IntegrationTests.OrderNotificationServiceTests;

/// <summary>
/// Exercises the orchestration service against real in-memory repositories and a faked provider gateway,
/// so the behaviour (notifications raised as the order moves, no-op gating, resend idempotency, failures not
/// failing the operation) is verified without any network call.
/// </summary>
public class OrderNotificationServiceTests
{
    private const string BuyerId = "buyer@example.com";
    private const string Number = "+15145550123";

    private readonly CatalogContext _context;
    private readonly ISmsGateway _gateway = Substitute.For<ISmsGateway>();
    private readonly OrderNotificationService _service;

    public OrderNotificationServiceTests()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new CatalogContext(options);

        var uriComposer = Substitute.For<IUriComposer>();
        uriComposer.ComposePicUri(Arg.Any<string>()).Returns(ci => ci.Arg<string>());

        // Sensible provider defaults.
        _gateway.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => new ProviderMessageResult("SM" + Guid.NewGuid().ToString("N"), "delivered",
                NotificationDeliveryState.Delivered, null, null, DateTimeOffset.UtcNow));
        _gateway.ScheduleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(_ => new ProviderMessageResult("SMsched" + Guid.NewGuid().ToString("N"), "scheduled",
                NotificationDeliveryState.Pending, null, null, null));
        _gateway.CancelScheduledAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => new ProviderMessageResult(ci.ArgAt<string>(0), "canceled",
                NotificationDeliveryState.Cancelled, null, null, null));

        _service = new OrderNotificationService(
            new EfRepository<Order>(_context),
            new EfRepository<CatalogItem>(_context),
            new EfRepository<ContactNumber>(_context),
            new EfRepository<OrderNotification>(_context),
            _gateway,
            uriComposer,
            new NotificationOptions { FollowUpDelayDays = 3 },
            Substitute.For<IAppLogger<OrderNotificationService>>());
    }

    private int SeedCatalogItem()
    {
        var item = new CatalogItem(1, 1, "desc", "Widget", 9.99m, "widget.png");
        _context.CatalogItems.Add(item);
        _context.SaveChanges();
        return item.Id;
    }

    private void SeedContactNumber() =>
        _context.ContactNumbers.Add(new ContactNumber(BuyerId, Number));

    [Fact]
    public async Task PlaceOrderWithNumberSendsPlacedNotification()
    {
        var itemId = SeedCatalogItem();
        SeedContactNumber();
        _context.SaveChanges();

        var order = await _service.PlaceOrderAsync(BuyerId, new[] { new OrderLineRequest(itemId, 2) }, CancellationToken.None);

        var notifications = _context.OrderNotifications.Where(n => n.OrderId == order.Id).ToList();
        Assert.Single(notifications);
        Assert.Equal(NotificationType.OrderPlaced, notifications[0].Type);
        Assert.Equal(NotificationDeliveryState.Delivered, notifications[0].DeliveryState);
        Assert.False(string.IsNullOrEmpty(notifications[0].ProviderMessageSid));
    }

    [Fact]
    public async Task PlaceOrderWithoutNumberSendsNothing()
    {
        var itemId = SeedCatalogItem();

        var order = await _service.PlaceOrderAsync(BuyerId, new[] { new OrderLineRequest(itemId, 1) }, CancellationToken.None);

        Assert.Empty(_context.OrderNotifications.Where(n => n.OrderId == order.Id).ToList());
        await _gateway.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchSendsMessageAndSchedulesFollowUp()
    {
        var itemId = SeedCatalogItem();
        SeedContactNumber();
        _context.SaveChanges();
        var order = await _service.PlaceOrderAsync(BuyerId, new[] { new OrderLineRequest(itemId, 1) }, CancellationToken.None);

        var result = await _service.DispatchOrderAsync(order.Id, CancellationToken.None);

        Assert.Equal(OrderTransition.Changed, result);
        var byType = _context.OrderNotifications.Where(n => n.OrderId == order.Id).ToList();
        Assert.Contains(byType, n => n.Type == NotificationType.OrderDispatched);
        Assert.Contains(byType, n => n.Type == NotificationType.DeliveryFollowUp && n.IsScheduledFollowUp);
        await _gateway.Received(1).ScheduleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RepeatDispatchIsNoOpAndSendsNothingExtra()
    {
        var itemId = SeedCatalogItem();
        SeedContactNumber();
        _context.SaveChanges();
        var order = await _service.PlaceOrderAsync(BuyerId, new[] { new OrderLineRequest(itemId, 1) }, CancellationToken.None);
        await _service.DispatchOrderAsync(order.Id, CancellationToken.None);
        var countAfterFirst = _context.OrderNotifications.Count(n => n.OrderId == order.Id);

        var second = await _service.DispatchOrderAsync(order.Id, CancellationToken.None);

        Assert.Equal(OrderTransition.NoChange, second);
        Assert.Equal(countAfterFirst, _context.OrderNotifications.Count(n => n.OrderId == order.Id));
    }

    [Fact]
    public async Task CancelAfterDispatchCancelsScheduledFollowUp()
    {
        var itemId = SeedCatalogItem();
        SeedContactNumber();
        _context.SaveChanges();
        var order = await _service.PlaceOrderAsync(BuyerId, new[] { new OrderLineRequest(itemId, 1) }, CancellationToken.None);
        await _service.DispatchOrderAsync(order.Id, CancellationToken.None);

        var result = await _service.CancelOrderAsync(order.Id, CancellationToken.None);

        Assert.Equal(OrderTransition.Changed, result);
        await _gateway.Received(1).CancelScheduledAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        var followUp = _context.OrderNotifications.Single(n => n.OrderId == order.Id && n.IsScheduledFollowUp);
        Assert.Equal(NotificationDeliveryState.Cancelled, followUp.DeliveryState);
    }

    [Fact]
    public async Task ResendUnderSameKeySendsOnce()
    {
        var itemId = SeedCatalogItem();
        SeedContactNumber();
        _context.SaveChanges();
        var order = await _service.PlaceOrderAsync(BuyerId, new[] { new OrderLineRequest(itemId, 1) }, CancellationToken.None);
        var original = _context.OrderNotifications.First(n => n.OrderId == order.Id);
        _gateway.ClearReceivedCalls();

        var first = await _service.ResendAsync(original.Id, "key-1", CancellationToken.None);
        var second = await _service.ResendAsync(original.Id, "key-1", CancellationToken.None);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first!.Id, second!.Id);
        await _gateway.Received(1).SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FreshKeyResendsAgain()
    {
        var itemId = SeedCatalogItem();
        SeedContactNumber();
        _context.SaveChanges();
        var order = await _service.PlaceOrderAsync(BuyerId, new[] { new OrderLineRequest(itemId, 1) }, CancellationToken.None);
        var original = _context.OrderNotifications.First(n => n.OrderId == order.Id);
        _gateway.ClearReceivedCalls();

        var first = await _service.ResendAsync(original.Id, "key-a", CancellationToken.None);
        var second = await _service.ResendAsync(original.Id, "key-b", CancellationToken.None);

        Assert.NotEqual(first!.Id, second!.Id);
        await _gateway.Received(2).SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendFailureDoesNotFailPlaceOrder()
    {
        var itemId = SeedCatalogItem();
        SeedContactNumber();
        _context.SaveChanges();
        _gateway.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<ProviderMessageResult>(_ => throw new ApplicationCore.Exceptions.SmsGatewayException("provider down", outcomeUnknown: true));

        var order = await _service.PlaceOrderAsync(BuyerId, new[] { new OrderLineRequest(itemId, 1) }, CancellationToken.None);

        Assert.True(order.Id > 0);   // the order was still placed
        var notification = _context.OrderNotifications.Single(n => n.OrderId == order.Id);
        Assert.Equal(NotificationDeliveryState.Unknown, notification.DeliveryState);
    }

    [Fact]
    public async Task ContactNumberIsScopedToOwner()
    {
        _context.ContactNumbers.Add(new ContactNumber(BuyerId, Number));
        _context.SaveChanges();
        var otherId = _context.ContactNumbers.Single().Id;

        var removedByOther = await _service.RemoveContactNumberAsync("someone-else@example.com", otherId, CancellationToken.None);

        Assert.False(removedByOther);
        Assert.Single(_context.ContactNumbers.ToList());
    }
}
