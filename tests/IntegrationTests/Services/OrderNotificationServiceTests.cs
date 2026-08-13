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
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.ApplicationCore.Sms;
using Microsoft.eShopWeb.Infrastructure.Data;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Services;

/// <summary>
/// Exercises the order-notification orchestration against a real in-memory repository with a
/// substituted messaging provider, focusing on the safety properties that live testing cannot
/// easily force (send never fails the operation, follow-up scheduling/cancellation, resend idempotency,
/// reconciliation classification).
/// </summary>
public class OrderNotificationServiceTests
{
    private const string Buyer = "buyer@example.com";

    private readonly CatalogContext _context;
    private readonly ISmsProvider _provider = Substitute.For<ISmsProvider>();
    private readonly OrderNotificationService _service;

    public OrderNotificationServiceTests()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new CatalogContext(options);

        var uriComposer = Substitute.For<IUriComposer>();
        uriComposer.ComposePicUri(Arg.Any<string>()).Returns("pic.png");

        _service = new OrderNotificationService(
            new EfRepository<Order>(_context),
            new EfRepository<CatalogItem>(_context),
            new EfRepository<Notification>(_context),
            new EfRepository<ContactNumber>(_context),
            _provider,
            uriComposer,
            Substitute.For<IAppLogger<OrderNotificationService>>());
    }

    private ProviderMessage SentMessage(string sid = "SM_sent") =>
        new(sid, "queued", null, null, null, "+1From", "body", null);

    private int SeedCatalogItem(decimal price = 10m)
    {
        var item = new CatalogItem(1, 1, "desc", "name", price, "pic.png");
        _context.CatalogItems.Add(item);
        _context.SaveChanges();
        return item.Id;
    }

    private void SeedContactNumber(string owner = Buyer, string number = "+15145550123")
    {
        _context.ContactNumbers.Add(new ContactNumber(owner, number));
        _context.SaveChanges();
    }

    private async Task<List<Notification>> NotificationsForOrder(int orderId) =>
        (await new EfRepository<Notification>(_context).ListAsync(new NotificationsByOrderSpecification(orderId))).ToList();

    [Fact]
    public async Task PlaceOrder_WithNumberOnFile_SendsPlacedNotification()
    {
        var itemId = SeedCatalogItem();
        SeedContactNumber();
        _provider.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SentMessage("SM_placed"));

        var order = await _service.PlaceOrderAsync(Buyer, new[] { new OrderLine(itemId, 2) }, DefaultAddress());

        Assert.True(order.Id > 0);
        var notifications = await NotificationsForOrder(order.Id);
        var placed = Assert.Single(notifications);
        Assert.Equal(NotificationKind.OrderPlaced, placed.Kind);
        Assert.Equal("SM_placed", placed.ProviderMessageSid);
        await _provider.Received(1).SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlaceOrder_WithNoNumberOnFile_DoesNotMessage()
    {
        var itemId = SeedCatalogItem();

        var order = await _service.PlaceOrderAsync(Buyer, new[] { new OrderLine(itemId, 1) }, DefaultAddress());

        Assert.True(order.Id > 0); // order still placed
        Assert.Empty(await NotificationsForOrder(order.Id));
        await _provider.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlaceOrder_WhenSendThrows_OrderStillPlacedAndFailureRecorded()
    {
        var itemId = SeedCatalogItem();
        SeedContactNumber();
        _provider.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("provider down"));

        var order = await _service.PlaceOrderAsync(Buyer, new[] { new OrderLine(itemId, 1) }, DefaultAddress());

        Assert.True(order.Id > 0); // operation succeeded despite send failure
        var placed = Assert.Single(await NotificationsForOrder(order.Id));
        Assert.Equal(Notification.StatusSendFailed, placed.DeliveryStatus);
        Assert.Null(placed.ProviderMessageSid);
    }

    [Fact]
    public async Task Dispatch_SchedulesDeliveryFollowUp()
    {
        var itemId = SeedCatalogItem();
        SeedContactNumber();
        _provider.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(SentMessage());
        _provider.ScheduleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new ProviderMessage("SM_sched", "scheduled", null, null, null, null, "body", null));
        var order = await _service.PlaceOrderAsync(Buyer, new[] { new OrderLine(itemId, 1) }, DefaultAddress());

        await _service.DispatchOrderAsync(order.Id);

        var followUp = (await NotificationsForOrder(order.Id)).Single(n => n.Kind == NotificationKind.DeliveryFollowUp);
        Assert.Equal("scheduled", followUp.DeliveryStatus);
        Assert.Equal("SM_sched", followUp.ProviderMessageSid);
        Assert.NotNull(followUp.ScheduledFor);
    }

    [Fact]
    public async Task Cancel_CallsOffScheduledFollowUp()
    {
        var itemId = SeedCatalogItem();
        SeedContactNumber();
        _provider.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(SentMessage());
        _provider.ScheduleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new ProviderMessage("SM_sched", "scheduled", null, null, null, null, "body", null));
        _provider.CancelScheduledAsync("SM_sched", Arg.Any<CancellationToken>())
            .Returns(new ProviderMessage("SM_sched", "canceled", null, null, null, null, "body", null));
        var order = await _service.PlaceOrderAsync(Buyer, new[] { new OrderLine(itemId, 1) }, DefaultAddress());
        await _service.DispatchOrderAsync(order.Id);

        await _service.CancelOrderAsync(order.Id);

        await _provider.Received(1).CancelScheduledAsync("SM_sched", Arg.Any<CancellationToken>());
        var followUp = (await NotificationsForOrder(order.Id)).Single(n => n.Kind == NotificationKind.DeliveryFollowUp);
        Assert.Equal("canceled", followUp.DeliveryStatus);
    }

    [Fact]
    public async Task Resend_IsIdempotentOnKey()
    {
        var itemId = SeedCatalogItem();
        SeedContactNumber();
        _provider.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(SentMessage("SM_placed"));
        var order = await _service.PlaceOrderAsync(Buyer, new[] { new OrderLine(itemId, 1) }, DefaultAddress());
        var source = (await NotificationsForOrder(order.Id)).Single();
        _provider.ClearReceivedCalls();

        var first = await _service.ResendAsync(source.Id, "key-1");
        var repeat = await _service.ResendAsync(source.Id, "key-1");
        var fresh = await _service.ResendAsync(source.Id, "key-2");

        Assert.Equal(first!.Id, repeat!.Id);      // same key -> same message
        Assert.NotEqual(first.Id, fresh!.Id);     // fresh key -> new message
        await _provider.Received(2).SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()); // only two real sends
    }

    [Fact]
    public async Task Reconcile_ClassifiesMatchedProviderOnlyAndEShopOnly()
    {
        var now = DateTimeOffset.UtcNow;
        var from = now.AddHours(-1);
        var to = now.AddHours(1);

        await SeedSentNotification("SM_match", now);
        await SeedSentNotification("SM_eshop_only", now);
        _provider.ListMessagesFromConfiguredNumberAsync(from, to, Arg.Any<CancellationToken>())
            .Returns(new List<ProviderMessage>
            {
                new("SM_match", "delivered", null, null, null, null, null, now),
                new("SM_provider_only", "sent", null, null, null, null, null, now)
            });

        var report = await _service.ReconcileAsync(from, to);

        Assert.Contains(report.Matched, m => m.Sid == "SM_match");
        Assert.Contains(report.ProviderOnly, m => m.Sid == "SM_provider_only");
        Assert.Contains(report.EShopOnly, m => m.Sid == "SM_eshop_only");
        Assert.Equal(1, report.MatchedCount);
        Assert.Equal(1, report.ProviderOnlyCount);
        Assert.Equal(1, report.EShopOnlyCount);
    }

    private async Task SeedSentNotification(string sid, DateTimeOffset dateSent)
    {
        var notification = new Notification(1, Buyer, NotificationKind.OrderPlaced, "+15145550123", "body");
        notification.RecordProviderResult(sid, "delivered", null, null, dateSent);
        await new EfRepository<Notification>(_context).AddAsync(notification);
    }

    private static Address DefaultAddress() => new("1 St", "City", "State", "Country", "12345");
}
