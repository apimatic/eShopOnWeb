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
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Notifications;

public class OrderNotificationServiceTests
{
    [Fact]
    public async Task ProviderFailureDoesNotUndoOrderAndResendIsIdempotent()
    {
        await using var db = CreateContext();
        var order = AddOrderAndContact(db);
        var provider = new FakeMessagingClient { FailSends = true };
        var service = new OrderNotificationService(db, provider,
            NullLogger<OrderNotificationService>.Instance);

        await service.NotifyOrderPlacedAsync(order);

        var failed = Assert.Single(db.OrderNotifications);
        Assert.Equal("failed", failed.ProviderStatus);
        Assert.NotNull(await db.Orders.FindAsync(order.Id));

        provider.FailSends = false;
        var first = await service.ResendAsync(failed, "same-key");
        var repeated = await service.ResendAsync(failed, "same-key");

        Assert.Equal(first.Id, repeated.Id);
        Assert.Equal(2, provider.SendCalls);
        Assert.Equal(2, await db.OrderNotifications.CountAsync());
    }

    [Fact]
    public async Task DispatchSchedulesAtProviderAndCancellationCancelsThatProviderMessage()
    {
        await using var db = CreateContext();
        var order = AddOrderAndContact(db);
        var provider = new FakeMessagingClient();
        var service = new OrderNotificationService(db, provider,
            NullLogger<OrderNotificationService>.Instance);

        order.Dispatch();
        await db.SaveChangesAsync();
        await service.NotifyOrderDispatchedAsync(order);
        var followUp = await db.OrderNotifications.SingleAsync(x => x.Kind == NotificationKind.DeliveryFollowUp);

        Assert.Equal("scheduled", followUp.ProviderStatus);
        Assert.True(followUp.ScheduledFor > DateTimeOffset.UtcNow.AddDays(2));

        order.Cancel();
        await db.SaveChangesAsync();
        await service.CancelOutstandingFollowUpsAsync(order);

        Assert.Equal("canceled", followUp.ProviderStatus);
        Assert.Equal(1, provider.CancelCalls);
    }

    [Fact]
    public async Task ContentDisposalRedactsProviderAndLocalBodyButKeepsRecord()
    {
        await using var db = CreateContext();
        var order = AddOrderAndContact(db);
        var provider = new FakeMessagingClient();
        var service = new OrderNotificationService(db, provider,
            NullLogger<OrderNotificationService>.Instance);
        await service.NotifyOrderPlacedAsync(order);
        var notification = Assert.Single(db.OrderNotifications);

        await service.DisposeContentAsync(notification);

        Assert.True(notification.IsContentDisposed);
        Assert.Null(notification.Body);
        Assert.NotNull(notification.ProviderMessageSid);
        Assert.Equal(1, provider.RedactCalls);
        Assert.Equal(1, await db.OrderNotifications.CountAsync());
    }

    private static CatalogContext CreateContext() => new(new DbContextOptionsBuilder<CatalogContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static Order AddOrderAndContact(CatalogContext db)
    {
        const string buyer = "shopper@example.test";
        var order = new Order(buyer, new Address("street", "city", "state", "country", "zip"),
            new List<OrderItem>
            {
                new(new CatalogItemOrdered(1, "item", "picture"), 10m, 1)
            });
        db.Orders.Add(order);
        db.ContactNumbers.Add(new ContactNumber(buyer, "+15550000002"));
        db.SaveChanges();
        return order;
    }

    private sealed class FakeMessagingClient : ITwilioMessagingClient
    {
        private readonly Dictionary<string, ProviderMessage> _messages = new();
        public bool FailSends { get; set; }
        public int SendCalls { get; private set; }
        public int CancelCalls { get; private set; }
        public int RedactCalls { get; private set; }

        public Task<ProviderMessage> SendAsync(string destination, string body, DateTimeOffset? sendAt = null,
            CancellationToken cancellationToken = default)
        {
            SendCalls++;
            if (FailSends) throw new TwilioProviderException("rejected", 12345, 400);
            var sid = "SM" + SendCalls.ToString("x32");
            var message = new ProviderMessage(sid, sendAt.HasValue ? "scheduled" : "queued", body,
                "+15550000001", destination, null, null, DateTimeOffset.UtcNow, null);
            _messages[sid] = message;
            return Task.FromResult(message);
        }

        public Task<ProviderMessage> FetchAsync(string messageSid,
            CancellationToken cancellationToken = default) => Task.FromResult(_messages[messageSid]);

        public Task<ProviderMessage> CancelAsync(string messageSid,
            CancellationToken cancellationToken = default)
        {
            CancelCalls++;
            var current = _messages[messageSid];
            return Task.FromResult(_messages[messageSid] = current with { Status = "canceled" });
        }

        public Task<ProviderMessage> RedactContentAsync(string messageSid,
            CancellationToken cancellationToken = default)
        {
            RedactCalls++;
            var current = _messages[messageSid];
            return Task.FromResult(_messages[messageSid] = current with { Body = string.Empty });
        }

        public Task<IReadOnlyList<ProviderMessage>> ListAsync(DateTimeOffset from, DateTimeOffset to,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProviderMessage>>(_messages.Values.ToList());
    }
}
