using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Notifications;

[TestClass]
public class OrderNotificationApplicationServiceTests
{
    [TestMethod]
    public async Task DispatchSchedulesAndCancelCallsOffFollowUpBeforeDeletingContact()
    {
        var (service, provider) = CreateService();
        var contactId = await service.RegisterContactNumberAsync("shopper", "input", CancellationToken.None);
        var orderId = await service.PlaceOrderAsync("shopper", OrderRequest(), CancellationToken.None);

        await service.DispatchOrderAsync(orderId, CancellationToken.None);
        await service.CancelOrderAsync(orderId, CancellationToken.None);
        var deleted = await service.DeleteContactNumberAsync("shopper", contactId, CancellationToken.None);
        var notifications = await service.GetOrderNotificationsAsync("shopper", orderId, CancellationToken.None);

        Assert.IsTrue(deleted);
        Assert.AreEqual(1, provider.ScheduleCalls);
        Assert.AreEqual(1, provider.CancelCalls);
        Assert.AreEqual(3, provider.SendCalls);
        Assert.IsTrue(notifications!.Any(x => x.Kind == "DeliveryFollowUp" && x.ProviderStatus == "canceled"));
        Assert.AreEqual(0, (await service.GetContactNumbersAsync("shopper", CancellationToken.None)).Count);
    }

    [TestMethod]
    public async Task ResendUsesApplicationIdempotencyKeyAndDisposedContentStaysUnavailable()
    {
        var (service, provider) = CreateService();
        provider.ImmediateStatus = "undelivered";
        await service.RegisterContactNumberAsync("shopper", "input", CancellationToken.None);
        var orderId = await service.PlaceOrderAsync("shopper", OrderRequest(), CancellationToken.None);
        var original = (await service.GetOrderNotificationsAsync("shopper", orderId, CancellationToken.None))!.Single();

        var first = await service.ResendAsync(original.NotificationId, "same-key", CancellationToken.None);
        var repeated = await service.ResendAsync(original.NotificationId, "same-key", CancellationToken.None);
        await service.DisposeContentAsync(original.NotificationId, CancellationToken.None);
        var refreshed = (await service.GetOrderNotificationsAsync("shopper", orderId, CancellationToken.None))!;

        Assert.AreEqual(first, repeated);
        Assert.AreEqual(2, provider.SendCalls);
        Assert.AreEqual(1, provider.RedactCalls);
        Assert.IsFalse(refreshed.Single(x => x.NotificationId == original.NotificationId).ContentAvailable);
    }

    private static (OrderNotificationApplicationService Service, FakeProvider Provider) CreateService()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new CatalogContext(options);
        db.CatalogItems.Add(new CatalogItem(1, 1, "description", "item", 12.50m, "picture.png"));
        db.SaveChanges();

        var provider = new FakeProvider();
        var settings = Options.Create(new TwilioSettings
        {
            AccountSid = "test-account",
            AuthToken = "test-token",
            FromNumber = "+15005550006",
            MessagingServiceSid = "test-messaging-service"
        });
        var uriComposer = new UriComposer(new CatalogSettings());
        var service = new OrderNotificationApplicationService(
            db,
            provider,
            uriComposer,
            settings,
            new NotificationIdempotencyLock(),
            NullLogger<OrderNotificationApplicationService>.Instance);
        return (service, provider);
    }

    private static PlaceOrderRequest OrderRequest() =>
        new() { Items = [new PlaceOrderItemRequest(1, 1)] };

    private sealed class FakeProvider : ITwilioMessagingService
    {
        private readonly Dictionary<string, ProviderMessage> _messages = new();
        private int _nextSid;

        public string ImmediateStatus { get; set; } = "queued";
        public int SendCalls { get; private set; }
        public int ScheduleCalls { get; private set; }
        public int CancelCalls { get; private set; }
        public int RedactCalls { get; private set; }

        public Task<string?> ValidateAndCanonicalizeAsync(string input, CancellationToken cancellationToken) =>
            Task.FromResult<string?>("+15005550006");

        public Task<ProviderMessage> SendAsync(string canonicalDestination, string body, CancellationToken cancellationToken)
        {
            SendCalls++;
            return Task.FromResult(Add(body, ImmediateStatus));
        }

        public Task<ProviderMessage> ScheduleAsync(string canonicalDestination, string body, DateTimeOffset sendAt, CancellationToken cancellationToken)
        {
            ScheduleCalls++;
            return Task.FromResult(Add(body, "scheduled"));
        }

        public Task<ProviderMessage> FetchAsync(string providerSid, CancellationToken cancellationToken) =>
            Task.FromResult(_messages[providerSid]);

        public Task<ProviderMessage> CancelAsync(string providerSid, CancellationToken cancellationToken)
        {
            CancelCalls++;
            var old = _messages[providerSid];
            var updated = old with { Status = "canceled", DateUpdated = DateTimeOffset.UtcNow };
            _messages[providerSid] = updated;
            return Task.FromResult(updated);
        }

        public Task<ProviderMessage> RedactAsync(string providerSid, CancellationToken cancellationToken)
        {
            RedactCalls++;
            var old = _messages[providerSid];
            var updated = old with { Body = string.Empty, DateUpdated = DateTimeOffset.UtcNow };
            _messages[providerSid] = updated;
            return Task.FromResult(updated);
        }

        public Task<ProviderMessagePage> ListAsync(DateTimeOffset fromExclusive, DateTimeOffset toExclusive, string? pageToken, CancellationToken cancellationToken) =>
            Task.FromResult(new ProviderMessagePage(_messages.Values.ToList(), null));

        private ProviderMessage Add(string body, string status)
        {
            var sid = $"SM_TEST_{++_nextSid}";
            var message = new ProviderMessage(
                sid, "+15005550006", body, status, null, null,
                DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow);
            _messages[sid] = message;
            return message;
        }
    }
}
