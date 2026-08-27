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
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Messaging;

public sealed class OrderNotificationServiceTests
{
    [Fact]
    public async Task ProviderFailureDoesNotRollBackPlacedOrder()
    {
        await using var db = CreateContext();
        var provider = Substitute.For<ISmsProvider>();
        provider.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns<Task<ProviderMessageState>>(_ => throw new SmsProviderException("message creation", 30001));
        var service = CreateService(db, provider);
        await SeedCatalogAndContactAsync(db);

        var result = await service.PlaceOrderAsync(
            "shopper@example.com",
            new[] { new OrderLine(1, 2) },
            Address(),
            CancellationToken.None);

        Assert.True(result.Order.Id > 0);
        Assert.Single(result.Notifications);
        Assert.Equal(NotificationDeliveryStatus.ProviderRequestFailed, result.Notifications[0].ProviderStatus);
        Assert.Equal(30001, result.Notifications[0].ProviderErrorCode);
        Assert.Single(await db.Orders.ToListAsync());
    }

    [Fact]
    public async Task CancellingDispatchedOrderCancelsProviderScheduledFollowUp()
    {
        await using var db = CreateContext();
        var provider = Substitute.For<ISmsProvider>();
        var sendNumber = 0;
        provider.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                sendNumber++;
                var scheduled = call.ArgAt<DateTimeOffset?>(2).HasValue;
                return Task.FromResult(State($"SM{sendNumber:D32}", scheduled ? "scheduled" : "delivered"));
            });
        provider.GetMessageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(State(call.ArgAt<string>(0), "scheduled")));
        provider.CancelScheduledMessageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(State(call.ArgAt<string>(0), "canceled")));
        var service = CreateService(db, provider);
        await SeedCatalogAndContactAsync(db);

        var placed = await service.PlaceOrderAsync(
            "shopper@example.com",
            new[] { new OrderLine(1, 1) },
            Address(),
            CancellationToken.None);
        var dispatched = await service.DispatchOrderAsync(placed.Order.Id, CancellationToken.None);
        var followUp = Assert.Single(dispatched.Notifications.Where(x => x.Kind == NotificationKind.DeliveryFollowUp));
        Assert.Equal("scheduled", followUp.ProviderStatus);

        var cancelled = await service.CancelOrderAsync(placed.Order.Id, CancellationToken.None);

        Assert.Equal(OrderProgressStatus.Cancelled, cancelled.Order.Status);
        Assert.Equal(0, cancelled.FollowUpCancellationFailures);
        Assert.Equal("canceled", followUp.ProviderStatus);
        await provider.Received(1).CancelScheduledMessageAsync(followUp.ProviderMessageSid!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResendIsAtMostOncePerIdempotencyKeyAndAllowsFreshKey()
    {
        await using var db = CreateContext();
        var provider = Substitute.For<ISmsProvider>();
        var resendNumber = 0;
        provider.GetMessageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(State(call.ArgAt<string>(0), "undelivered")));
        provider.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(State($"SM{++resendNumber:D32}", "queued")));
        var service = CreateService(db, provider);
        var source = await SeedFailedNotificationAsync(db);

        var first = await service.ResendAsync(source.Id, "attempt-one", CancellationToken.None);
        var repeated = await service.ResendAsync(source.Id, "attempt-one", CancellationToken.None);
        var secondAttempt = await service.ResendAsync(source.Id, "attempt-two", CancellationToken.None);

        Assert.Equal(first.Id, repeated.Id);
        Assert.NotEqual(first.Id, secondAttempt.Id);
        await provider.Received(2).SendAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OrderIsNotMarkedCancelledWhenProviderCannotCancelFollowUp()
    {
        await using var db = CreateContext();
        var provider = Substitute.For<ISmsProvider>();
        var sendNumber = 0;
        provider.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                sendNumber++;
                var scheduled = call.ArgAt<DateTimeOffset?>(2).HasValue;
                return Task.FromResult(State($"SM{sendNumber:D32}", scheduled ? "scheduled" : "delivered"));
            });
        provider.GetMessageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(State(call.ArgAt<string>(0), "scheduled")));
        provider.CancelScheduledMessageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<ProviderMessageState>>(_ => throw new SmsProviderException("scheduled-message cancellation"));
        var service = CreateService(db, provider);
        await SeedCatalogAndContactAsync(db);
        var placed = await service.PlaceOrderAsync(
            "shopper@example.com",
            new[] { new OrderLine(1, 1) },
            Address(),
            CancellationToken.None);
        await service.DispatchOrderAsync(placed.Order.Id, CancellationToken.None);

        await Assert.ThrowsAsync<SmsProviderException>(() =>
            service.CancelOrderAsync(placed.Order.Id, CancellationToken.None));

        Assert.Equal(OrderProgressStatus.Dispatched, placed.Order.Status);
        await provider.Received(3).CancelScheduledMessageAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private static CatalogContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new CatalogContext(options);
    }

    private static OrderNotificationService CreateService(CatalogContext db, ISmsProvider provider)
    {
        var uriComposer = Substitute.For<IUriComposer>();
        uriComposer.ComposePicUri(Arg.Any<string>()).Returns("https://example.test/item.png");
        return new OrderNotificationService(
            db,
            provider,
            uriComposer,
            TimeProvider.System,
            NullLogger<OrderNotificationService>.Instance);
    }

    private static async Task SeedCatalogAndContactAsync(CatalogContext db)
    {
        db.CatalogItems.Add(new CatalogItem(1, 1, "Description", "Item", 12.50m, "item.png"));
        db.ContactNumbers.Add(new ContactNumber("shopper@example.com", "+15555550100", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
    }

    private static async Task<OrderNotification> SeedFailedNotificationAsync(CatalogContext db)
    {
        var contact = new ContactNumber("shopper@example.com", "+15555550100", DateTimeOffset.UtcNow);
        var order = new Order("shopper@example.com", Address(), new List<OrderItem>
        {
            new(new CatalogItemOrdered(1, "Item", "item.png"), 12.50m, 1)
        });
        db.AddRange(contact, order);
        await db.SaveChangesAsync();
        var notification = new OrderNotification(
            order.Id,
            contact.Id,
            "shopper@example.com",
            NotificationKind.OrderPlaced,
            "Placed",
            DateTimeOffset.UtcNow);
        notification.RecordProviderState(State("SM99999999999999999999999999999999", "undelivered"), DateTimeOffset.UtcNow);
        db.OrderNotifications.Add(notification);
        await db.SaveChangesAsync();
        return notification;
    }

    private static Address Address() => new("1 Main St", "Toronto", "ON", "Canada", "A1A 1A1");

    private static ProviderMessageState State(string sid, string status) =>
        new(sid, status, null, DateTimeOffset.UtcNow, status == "scheduled" ? null : DateTimeOffset.UtcNow);
}
