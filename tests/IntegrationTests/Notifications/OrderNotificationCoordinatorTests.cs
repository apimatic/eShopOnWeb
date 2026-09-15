using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Notifications;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Notifications;

public class OrderNotificationCoordinatorTests
{
    [Fact]
    public async Task PlaceOrderSucceedsAndRecordsFailureWhenProviderRejectsSend()
    {
        await using var db = NewContext();
        await SeedCatalogAndContactAsync(db);
        var twilio = new FakeTwilioClient { FailSends = true };
        var coordinator = new OrderNotificationCoordinator(db, twilio);

        var order = await coordinator.PlaceOrderAsync(
            "shopper@example.com",
            new[] { new OrderLineInput(1, 2) },
            null,
            CancellationToken.None);

        Assert.True(order.Id > 0);
        var notification = Assert.Single(await db.OrderNotifications.ToListAsync());
        Assert.Equal("failed", notification.ProviderStatus);
        Assert.Null(notification.ProviderMessageSid);
    }

    [Fact]
    public async Task CancelCallsProviderForScheduledFollowUp()
    {
        await using var db = NewContext();
        await SeedCatalogAndContactAsync(db);
        var twilio = new FakeTwilioClient();
        var coordinator = new OrderNotificationCoordinator(db, twilio);
        var order = await coordinator.PlaceOrderAsync(
            "shopper@example.com",
            new[] { new OrderLineInput(1, 1) },
            null,
            CancellationToken.None);

        Assert.Equal(OrderActionResult.Success, await coordinator.DispatchOrderAsync(order.Id, CancellationToken.None));
        var followUp = await db.OrderNotifications.SingleAsync(x => x.Kind == NotificationKind.DeliveryFollowUp);
        Assert.NotNull(followUp.ScheduledFor);

        Assert.Equal(OrderActionResult.Success, await coordinator.CancelOrderAsync(order.Id, CancellationToken.None));
        Assert.Contains(followUp.ProviderMessageSid!, twilio.CancelledSids);
        Assert.Equal("canceled", followUp.ProviderStatus);
    }

    [Fact]
    public async Task RepeatingResendIdempotencyKeySendsOnlyOnce()
    {
        await using var db = NewContext();
        await SeedCatalogAndContactAsync(db);
        var twilio = new FakeTwilioClient();
        var coordinator = new OrderNotificationCoordinator(db, twilio);
        var order = await coordinator.PlaceOrderAsync(
            "shopper@example.com",
            new[] { new OrderLineInput(1, 1) },
            null,
            CancellationToken.None);
        var contact = await db.ContactNumbers.SingleAsync();
        var failed = new OrderNotification(
            order.Id,
            contact.Id,
            NotificationKind.OrderPlaced,
            "Retry me",
            DateTimeOffset.UtcNow);
        failed.RecordSendFailure(500, "Provider failed.", DateTimeOffset.UtcNow);
        db.OrderNotifications.Add(failed);
        await db.SaveChangesAsync();
        var sendsBefore = twilio.Sends.Count;

        var first = await coordinator.ResendAsync(failed.Id, "same-key", CancellationToken.None);
        var second = await coordinator.ResendAsync(failed.Id, "same-key", CancellationToken.None);

        Assert.Equal(ResendOutcome.Success, first.Outcome);
        Assert.Equal(first.NotificationId, second.NotificationId);
        Assert.Equal(sendsBefore + 1, twilio.Sends.Count);
    }

    private static CatalogContext NewContext() => new(
        new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static async Task SeedCatalogAndContactAsync(CatalogContext db)
    {
        db.CatalogItems.Add(new CatalogItem(1, 1, "Description", "Product", 10m, "product.png"));
        db.ContactNumbers.Add(new ContactNumber("shopper@example.com", "+15555550100", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
    }

    private sealed class FakeTwilioClient : ITwilioClient
    {
        private int _sid;
        public bool FailSends { get; set; }
        public List<(string To, string Body, DateTimeOffset? SendAt)> Sends { get; } = new();
        public List<string> CancelledSids { get; } = new();

        public Task<TwilioPhoneLookup> LookupPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken) =>
            Task.FromResult(new TwilioPhoneLookup(true, phoneNumber));

        public Task<TwilioMessageRecord> SendMessageAsync(string to, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken)
        {
            Sends.Add((to, body, sendAt));
            if (FailSends) throw new TwilioProviderException(500, "Provider failed.");
            return Task.FromResult(Message($"SM{++_sid:00000000000000000000000000000000}", sendAt is null ? "queued" : "scheduled"));
        }

        public Task<TwilioMessageRecord> FetchMessageAsync(string messageSid, CancellationToken cancellationToken) =>
            Task.FromResult(Message(messageSid, "undelivered"));

        public Task<TwilioMessageRecord> CancelMessageAsync(string messageSid, CancellationToken cancellationToken)
        {
            CancelledSids.Add(messageSid);
            return Task.FromResult(Message(messageSid, "canceled"));
        }

        public Task<TwilioMessageRecord> RedactMessageAsync(string messageSid, CancellationToken cancellationToken) =>
            Task.FromResult(Message(messageSid, "delivered"));

        public Task<IReadOnlyList<TwilioMessageRecord>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TwilioMessageRecord>>(Array.Empty<TwilioMessageRecord>());

        private static TwilioMessageRecord Message(string sid, string status) =>
            new(sid, "body", "+15555550101", "+15555550100", status, null, null, DateTimeOffset.UtcNow, null);
    }
}
