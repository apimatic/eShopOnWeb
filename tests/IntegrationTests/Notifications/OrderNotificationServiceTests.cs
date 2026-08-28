#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Notifications;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Notifications;

public sealed class OrderNotificationServiceTests
{
    [Fact]
    public async Task RejectsAProviderInvalidDestinationWithoutPersistingIt()
    {
        await using var db = CreateDb();
        var service = CreateService(db, new FakeGateway { CanonicalResult = null });

        await Assert.ThrowsAsync<InvalidContactNumberException>(() =>
            service.RegisterContactNumberAsync("shopper", "typed", null, default));
        Assert.Empty(await db.ContactNumbers.ToListAsync());
    }

    [Fact]
    public async Task ProviderFailureDoesNotRollBackPlacedOrder()
    {
        await using var db = CreateDb();
        db.CatalogItems.Add(new CatalogItem(1, 1, "description", "item", 12m, "item.png"));
        await db.SaveChangesAsync();
        var gateway = new FakeGateway { FailSends = true };
        var service = CreateService(db, gateway);
        await service.RegisterContactNumberAsync("shopper", "typed", null, default);

        var orderId = await service.PlaceOrderAsync("shopper", Command(), default);

        Assert.True(await db.Orders.AnyAsync(x => x.Id == orderId));
        var notification = await db.OrderNotifications.SingleAsync();
        Assert.Equal("failed", notification.ProviderStatus);
    }

    [Fact]
    public async Task DispatchSchedulesAtProviderAndCancelCallsItOff()
    {
        await using var db = CreateDb();
        db.CatalogItems.Add(new CatalogItem(1, 1, "description", "item", 12m, "item.png"));
        await db.SaveChangesAsync();
        var gateway = new FakeGateway();
        var service = CreateService(db, gateway);
        await service.RegisterContactNumberAsync("shopper", "typed", null, default);
        var orderId = await service.PlaceOrderAsync("shopper", Command(), default);

        Assert.True(await service.DispatchOrderAsync(orderId, default));
        Assert.Single(gateway.ScheduledFor);
        Assert.InRange(gateway.ScheduledFor[0], DateTimeOffset.UtcNow.AddDays(2.99), DateTimeOffset.UtcNow.AddDays(3.01));

        Assert.True(await service.CancelOrderAsync(orderId, default));
        Assert.Single(gateway.Cancelled);
        var followUp = await db.OrderNotifications.SingleAsync(x => x.Kind ==
            ApplicationCore.Entities.NotificationAggregate.NotificationKind.DeliveryFollowUp);
        Assert.NotNull(followUp.CancellationCompletedAt);
        Assert.Equal("canceled", followUp.ProviderStatus);
    }

    [Fact]
    public async Task ResendUsesPersistedIdempotencyClaim()
    {
        await using var db = CreateDb();
        db.CatalogItems.Add(new CatalogItem(1, 1, "description", "item", 12m, "item.png"));
        await db.SaveChangesAsync();
        var gateway = new FakeGateway { FailSends = true };
        var service = CreateService(db, gateway);
        await service.RegisterContactNumberAsync("shopper", "typed", null, default);
        await service.PlaceOrderAsync("shopper", Command(), default);
        var failed = await db.OrderNotifications.SingleAsync();
        gateway.FailSends = false;

        var first = await service.ResendAsync(failed.Id, "same-key", default);
        var repeated = await service.ResendAsync(failed.Id, "same-key", default);

        Assert.Equal(first, repeated);
        Assert.Equal(2, gateway.SendAttempts); // failed original + one resend, never a repeat.
        Assert.Equal(2, await db.OrderNotifications.CountAsync());
    }

    [Fact]
    public async Task ShopperDataIsOwnershipScoped()
    {
        await using var db = CreateDb();
        db.CatalogItems.Add(new CatalogItem(1, 1, "description", "item", 12m, "item.png"));
        await db.SaveChangesAsync();
        var service = CreateService(db, new FakeGateway());
        var contact = await service.RegisterContactNumberAsync("shopper", "typed", null, default);
        var orderId = await service.PlaceOrderAsync("shopper", Command(), default);

        Assert.Empty(await service.GetContactNumbersAsync("other-shopper", default));
        Assert.False(await service.DeleteContactNumberAsync("other-shopper", contact.ContactNumberId, default));
        Assert.Null(await service.GetOrderNotificationsAsync("other-shopper", orderId, default));
    }

    private static CatalogContext CreateDb() => new(new DbContextOptionsBuilder<CatalogContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static OrderNotificationService CreateService(CatalogContext db, FakeGateway gateway) =>
        new(db, gateway, Substitute.For<ILogger<OrderNotificationService>>());

    private static PlaceOrderCommand Command() => new(
        new[] { new PlaceOrderLine(1, 2) },
        new ShippingAddress("street", "city", "state", "country", "zip"));

    private sealed class FakeGateway : ITwilioMessagingGateway
    {
        private int _nextId;
        public bool FailSends { get; set; }
        public string? CanonicalResult { get; set; } = "+15555550100";
        public int SendAttempts { get; private set; }
        public List<DateTimeOffset> ScheduledFor { get; } = new();
        public List<string> Cancelled { get; } = new();

        public Task<string?> ValidateAndCanonicalizeAsync(string number, string? countryCode, CancellationToken ct) =>
            Task.FromResult(CanonicalResult);

        public Task<ProviderMessage> SendAsync(string destination, string content, CancellationToken ct)
        {
            SendAttempts++;
            if (FailSends) throw new TwilioProviderException("rejected");
            return Task.FromResult(Message("queued"));
        }

        public Task<ProviderMessage> ScheduleAsync(string destination, string content, DateTimeOffset sendAt, CancellationToken ct)
        {
            ScheduledFor.Add(sendAt);
            return Task.FromResult(Message("scheduled"));
        }

        public Task<ProviderMessage> CancelAsync(string providerMessageId, CancellationToken ct)
        {
            Cancelled.Add(providerMessageId);
            return Task.FromResult(new ProviderMessage(providerMessageId, "canceled", null, null, null,
                DateTimeOffset.UtcNow, null));
        }

        public Task<ProviderMessage> FetchAsync(string providerMessageId, CancellationToken ct) =>
            Task.FromResult(new ProviderMessage(providerMessageId, "undelivered", null, null,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null));

        public Task<ProviderMessage> RedactAsync(string providerMessageId, CancellationToken ct) =>
            Task.FromResult(new ProviderMessage(providerMessageId, "delivered", null, null,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, string.Empty));

        public Task<IReadOnlyList<ProviderMessage>> ListAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ProviderMessage>>(Array.Empty<ProviderMessage>());

        private ProviderMessage Message(string status) => new($"SM{++_nextId:0000}", status, null,
            DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow, null);
    }
}
