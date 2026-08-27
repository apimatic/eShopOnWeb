using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Notifications;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.OrderNotifications;

[TestClass]
public class OrderNotificationApplicationServiceTests
{
    [TestMethod]
    public async Task DispatchThenCancel_CancelsProviderFollowUpBeforeCancellingOrder()
    {
        await using var db = CreateContext();
        var provider = new FakeTwilioClient();
        var service = new OrderNotificationApplicationService(db, provider);
        await service.RegisterContactAsync("shopper", "typed-number", CancellationToken.None);

        var placed = await service.PlaceOrderAsync("shopper", Request(), CancellationToken.None);
        await service.DispatchOrderAsync(placed.OrderId, CancellationToken.None);

        var followUp = await db.OrderNotifications.SingleAsync(x => x.Kind == NotificationKind.DeliveryFollowUp);
        Assert.AreEqual("scheduled", followUp.ProviderStatus);

        await service.CancelOrderAsync(placed.OrderId, CancellationToken.None);

        Assert.AreEqual("canceled", provider.Messages[followUp.ProviderMessageSid!].Status);
        Assert.AreEqual(OrderStatus.Cancelled, (await db.Orders.SingleAsync()).Status);
        Assert.AreEqual(4, provider.SendCount);
    }

    [TestMethod]
    public async Task PlaceOrder_WhenProviderRejectsMessage_StillCreatesOrderAndFailureRecord()
    {
        await using var db = CreateContext();
        var provider = new FakeTwilioClient { RejectSends = true };
        var service = new OrderNotificationApplicationService(db, provider);
        await service.RegisterContactAsync("shopper", "typed-number", CancellationToken.None);

        var response = await service.PlaceOrderAsync("shopper", Request(), CancellationToken.None);

        Assert.IsTrue(response.OrderId > 0);
        Assert.AreEqual(1, await db.Orders.CountAsync());
        Assert.AreEqual("failed", (await db.OrderNotifications.SingleAsync()).ProviderStatus);
    }

    [TestMethod]
    public async Task Resend_WithSameIdempotencyKey_SendsOnlyOnceAndReturnsSameIdentifier()
    {
        await using var db = CreateContext();
        var provider = new FakeTwilioClient();
        var service = new OrderNotificationApplicationService(db, provider);
        await service.RegisterContactAsync("shopper", "typed-number", CancellationToken.None);
        var placed = await service.PlaceOrderAsync("shopper", Request(), CancellationToken.None);
        var original = await db.OrderNotifications.SingleAsync();
        provider.SetStatus(original.ProviderMessageSid!, "undelivered");

        var first = await service.ResendAsync(original.Id, "same-key", CancellationToken.None);
        var repeated = await service.ResendAsync(original.Id, "same-key", CancellationToken.None);

        Assert.AreEqual(first.NotificationId, repeated.NotificationId);
        Assert.AreEqual(2, provider.SendCount);
        Assert.AreEqual(2, await db.OrderNotifications.CountAsync());
    }

    [TestMethod]
    public async Task DisposeContent_RedactsProviderAndApplicationCopies()
    {
        await using var db = CreateContext();
        var provider = new FakeTwilioClient();
        var service = new OrderNotificationApplicationService(db, provider);
        await service.RegisterContactAsync("shopper", "typed-number", CancellationToken.None);
        await service.PlaceOrderAsync("shopper", Request(), CancellationToken.None);
        var notification = await db.OrderNotifications.SingleAsync();

        await service.DisposeContentAsync(notification.Id, CancellationToken.None);

        Assert.IsNull(provider.Messages[notification.ProviderMessageSid!].Body);
        Assert.IsNull(notification.Body);
        Assert.IsNotNull(notification.ContentDisposedAt);
    }

    [TestMethod]
    public async Task ShopperQueries_AreScopedByBuyerIdentity()
    {
        await using var db = CreateContext();
        var provider = new FakeTwilioClient();
        var service = new OrderNotificationApplicationService(db, provider);
        await service.RegisterContactAsync("shopper-a", "typed-number", CancellationToken.None);
        var order = await service.PlaceOrderAsync("shopper-a", Request(), CancellationToken.None);

        Assert.AreEqual(0, (await service.GetMyOrdersAsync("shopper-b", CancellationToken.None)).Count);
        await Assert.ThrowsExceptionAsync<NotificationApiException>(
            () => service.GetOrderNotificationsAsync("shopper-b", order.OrderId, CancellationToken.None));
    }

    private static CatalogContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new CatalogContext(options);
        db.CatalogItems.Add(new CatalogItem(1, 1, "description", "item", 12.50m, "item.png"));
        db.SaveChanges();
        return db;
    }

    private static PlaceOrderRequest Request() => new()
    {
        Items = new List<PlaceOrderItemRequest>
        {
            new() { CatalogItemId = 1, Quantity = 2 }
        }
    };

    private sealed class FakeTwilioClient : ITwilioMessagingClient
    {
        private int _nextId;
        public bool RejectSends { get; set; }
        public int SendCount { get; private set; }
        public Dictionary<string, ProviderMessage> Messages { get; } = new();

        public Task<PhoneValidationResult> ValidatePhoneNumberAsync(string number, CancellationToken cancellationToken)
            => Task.FromResult(new PhoneValidationResult(true, "+15550001111"));

        public Task<ProviderMessage> SendAsync(
            string to,
            string body,
            DateTimeOffset? sendAt,
            CancellationToken cancellationToken)
        {
            SendCount++;
            if (RejectSends)
            {
                throw new TwilioProviderException(400, 21610);
            }

            var now = DateTimeOffset.UtcNow;
            var message = new ProviderMessage(
                $"SM{++_nextId:D32}",
                sendAt.HasValue ? "scheduled" : "queued",
                "+15551112222",
                to,
                body,
                now,
                null,
                now,
                null);
            Messages.Add(message.Sid, message);
            return Task.FromResult(message);
        }

        public Task<ProviderMessage> GetAsync(string messageSid, CancellationToken cancellationToken)
            => Task.FromResult(Messages[messageSid]);

        public Task<ProviderMessage> CancelAsync(string messageSid, CancellationToken cancellationToken)
        {
            SetStatus(messageSid, "canceled");
            return Task.FromResult(Messages[messageSid]);
        }

        public Task RedactContentAsync(string messageSid, CancellationToken cancellationToken)
        {
            Messages[messageSid] = Messages[messageSid] with { Body = null, DateUpdated = DateTimeOffset.UtcNow };
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ProviderMessage>> ListFromNumberAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ProviderMessage>>(Messages.Values.ToList());

        public void SetStatus(string sid, string status)
        {
            Messages[sid] = Messages[sid] with { Status = status, DateUpdated = DateTimeOffset.UtcNow };
        }
    }
}
