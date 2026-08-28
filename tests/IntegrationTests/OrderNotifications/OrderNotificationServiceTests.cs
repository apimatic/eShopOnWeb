using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.eShopWeb.Infrastructure.Twilio;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.OrderNotifications;

public sealed class OrderNotificationServiceTests
{
    [Fact]
    public async Task PlacesDispatchesSchedulesAndCancelsWithoutParallelOrderModel()
    {
        await using var db = NewContext();
        var item = new CatalogItem(1, 1, "description", "item", 12.50m, "picture.png");
        db.CatalogItems.Add(item);
        var contact = new ContactNumber("shopper", "canonical-destination");
        db.ContactNumbers.Add(contact);
        await db.SaveChangesAsync();
        var gateway = new FakeGateway();
        var service = new OrderNotificationService(db, gateway);

        var orderId = await service.PlaceOrderAsync(
            "shopper",
            [new OrderLineInput(item.Id, 2)],
            Address(),
            CancellationToken.None);
        Assert.Equal(OrderStatus.Placed, (await db.Orders.FindAsync(orderId))!.Status);
        Assert.Equal(1, gateway.SendCount);

        Assert.True(await service.DispatchOrderAsync(orderId, CancellationToken.None));
        Assert.Equal(OrderStatus.Dispatched, (await db.Orders.FindAsync(orderId))!.Status);
        Assert.Equal(2, gateway.SendCount);
        Assert.Equal(1, gateway.ScheduleCount);
        Assert.True(gateway.LastScheduledFor > DateTimeOffset.UtcNow.AddDays(2));

        Assert.True(await service.CancelOrderAsync(orderId, CancellationToken.None));
        Assert.Equal(OrderStatus.Cancelled, (await db.Orders.FindAsync(orderId))!.Status);
        Assert.Equal(1, gateway.CancelCount);
        Assert.Equal(3, gateway.SendCount);
        Assert.Equal("canceled", (await db.OrderNotifications.SingleAsync(x => x.Kind == NotificationKind.DeliveryFollowUp)).ProviderStatus);
    }

    [Fact]
    public async Task ProviderFailureDoesNotRollBackOrder()
    {
        await using var db = NewContext();
        var item = new CatalogItem(1, 1, "description", "item", 5m, "picture.png");
        db.CatalogItems.Add(item);
        db.ContactNumbers.Add(new ContactNumber("shopper", "canonical-destination"));
        await db.SaveChangesAsync();
        var gateway = new FakeGateway { FailSends = true };
        var service = new OrderNotificationService(db, gateway);

        var orderId = await service.PlaceOrderAsync(
            "shopper",
            [new OrderLineInput(item.Id, 1)],
            Address(),
            CancellationToken.None);

        Assert.True(await db.Orders.AnyAsync(x => x.Id == orderId));
        Assert.Equal("provider_error", (await db.OrderNotifications.SingleAsync()).ProviderStatus);
    }

    [Fact]
    public async Task CancellationFailureDoesNotRollBackCancellationAndIsQueuedForRetry()
    {
        await using var db = NewContext();
        var item = new CatalogItem(1, 1, "description", "item", 5m, "picture.png");
        db.CatalogItems.Add(item);
        db.ContactNumbers.Add(new ContactNumber("shopper", "canonical-destination"));
        await db.SaveChangesAsync();
        var gateway = new FakeGateway();
        var service = new OrderNotificationService(db, gateway);
        var orderId = await service.PlaceOrderAsync(
            "shopper",
            [new OrderLineInput(item.Id, 1)],
            Address(),
            CancellationToken.None);
        await service.DispatchOrderAsync(orderId, CancellationToken.None);
        gateway.FailCancellations = true;

        Assert.True(await service.CancelOrderAsync(orderId, CancellationToken.None));

        Assert.Equal(OrderStatus.Cancelled, (await db.Orders.FindAsync(orderId))!.Status);
        Assert.True((await db.OrderNotifications.SingleAsync(x => x.Kind == NotificationKind.DeliveryFollowUp)).CancellationPending);
    }

    [Fact]
    public async Task RepeatingResendKeyReturnsSameNotificationWithoutSecondSend()
    {
        await using var db = NewContext();
        var order = new Order("shopper", Address(), []);
        db.Orders.Add(order);
        var contact = new ContactNumber("shopper", "canonical-destination");
        db.ContactNumbers.Add(contact);
        await db.SaveChangesAsync();
        var failed = new OrderNotification(order.Id, "shopper", contact.Id, NotificationKind.OrderPlaced, "body");
        failed.ApplyProviderState(State("SM-original", "failed"));
        db.OrderNotifications.Add(failed);
        await db.SaveChangesAsync();
        var gateway = new FakeGateway();
        var service = new OrderNotificationService(db, gateway);

        var first = await service.ResendAsync(failed.Id, "same-key", CancellationToken.None);
        var repeated = await service.ResendAsync(failed.Id, "same-key", CancellationToken.None);
        var fresh = await service.ResendAsync(failed.Id, "fresh-key", CancellationToken.None);

        Assert.Equal(first, repeated);
        Assert.NotEqual(first, fresh);
        Assert.Equal(2, gateway.SendCount);
        Assert.Equal(3, await db.OrderNotifications.CountAsync());
    }

    [Fact]
    public async Task ContentDisposalRedactsProviderBeforeRemovingLocalText()
    {
        await using var db = NewContext();
        var order = new Order("shopper", Address(), []);
        db.Orders.Add(order);
        var contact = new ContactNumber("shopper", "canonical-destination");
        db.ContactNumbers.Add(contact);
        await db.SaveChangesAsync();
        var notification = new OrderNotification(order.Id, "shopper", contact.Id, NotificationKind.OrderPlaced, "private body");
        notification.ApplyProviderState(State("SM-redact", "delivered"));
        db.OrderNotifications.Add(notification);
        await db.SaveChangesAsync();
        var gateway = new FakeGateway();
        var service = new OrderNotificationService(db, gateway);

        Assert.True(await service.DisposeContentAsync(notification.Id, CancellationToken.None));

        Assert.Equal(1, gateway.RedactCount);
        Assert.True(notification.ContentDisposed);
        Assert.Null(notification.Body);
        Assert.Equal("delivered", notification.ProviderStatus);
    }

    [Fact]
    public async Task ContactOwnershipIsEnforced()
    {
        await using var db = NewContext();
        var contact = new ContactNumber("owner", "canonical-destination");
        db.ContactNumbers.Add(contact);
        await db.SaveChangesAsync();
        var service = new OrderNotificationService(db, new FakeGateway());

        Assert.False(await service.DeleteContactNumberAsync("different-shopper", contact.Id, CancellationToken.None));
        Assert.Empty(await service.GetContactNumbersAsync("different-shopper", CancellationToken.None));
        Assert.True(await db.ContactNumbers.AnyAsync(x => x.Id == contact.Id));
    }

    private static CatalogContext NewContext() => new(new DbContextOptionsBuilder<CatalogContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
        .Options);

    private static Address Address() => new("street", "city", "state", "country", "zip");

    private static ProviderMessageState State(string sid, string status) =>
        new(sid, status, null, null, DateTimeOffset.UtcNow.ToString("O"), null, null, null, null);

    private sealed class FakeGateway : ITwilioMessagingGateway
    {
        private int _sid;
        public bool FailSends { get; init; }
        public bool FailCancellations { get; set; }
        public int SendCount { get; private set; }
        public int ScheduleCount { get; private set; }
        public int CancelCount { get; private set; }
        public int RedactCount { get; private set; }
        public DateTimeOffset? LastScheduledFor { get; private set; }

        public Task<string> ValidateAndCanonicalizeAsync(string phoneNumber, CancellationToken ct) => Task.FromResult("canonical-destination");

        public Task<ProviderMessageState> SendAsync(string to, string body, CancellationToken ct)
        {
            SendCount++;
            if (FailSends)
            {
                throw new NotificationProviderException("failed");
            }

            return Task.FromResult(State($"SM-send-{++_sid}", "queued"));
        }

        public Task<ProviderMessageState> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken ct)
        {
            ScheduleCount++;
            LastScheduledFor = sendAt;
            return Task.FromResult(State($"SM-scheduled-{++_sid}", "scheduled"));
        }

        public Task<ProviderMessageState> FetchAsync(string providerMessageSid, CancellationToken ct) =>
            Task.FromResult(State(providerMessageSid, providerMessageSid == "SM-original" ? "failed" : "delivered"));

        public Task<ProviderMessageState> CancelAsync(string providerMessageSid, CancellationToken ct)
        {
            CancelCount++;
            if (FailCancellations)
            {
                throw new NotificationProviderException("failed");
            }

            return Task.FromResult(State(providerMessageSid, "canceled"));
        }

        public Task<ProviderMessageState> RedactAsync(string providerMessageSid, CancellationToken ct)
        {
            RedactCount++;
            return Task.FromResult(State(providerMessageSid, "delivered"));
        }

        public Task<IReadOnlyList<ProviderMessageState>> ListAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ProviderMessageState>>([]);
    }
}
