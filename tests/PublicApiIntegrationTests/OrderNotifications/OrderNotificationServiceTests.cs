using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.OrderNotifications;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.OrderNotifications;

[TestClass]
public class OrderNotificationServiceTests
{
    [TestMethod]
    public async Task DispatchCancelResendAndRedactRemainProviderBackedAndIdempotent()
    {
        await using var db = NewContext();
        db.CatalogItems.Add(new CatalogItem(1, 1, "description", "item", 12m, "picture"));
        await db.SaveChangesAsync();
        var provider = new FakeTwilioClient { NewMessageStatus = "undelivered" };
        var service = new OrderNotificationService(db, provider, TimeProvider.System);

        var contact = await service.RegisterContactNumberAsync("shopper",
            new RegisterContactNumberRequest("test-destination"), CancellationToken.None);
        var order = await service.PlaceOrderAsync("shopper",
            new PlaceOrderRequest(new[] { new PlaceOrderItemRequest(1, 2) }),
            CancellationToken.None);
        var placed = await db.OrderNotifications.SingleAsync(x =>
            x.OrderId == order.OrderId && x.Kind == NotificationKind.OrderPlaced);

        var firstResend = await service.ResendAsync(placed.Id,
            new ResendNotificationRequest("same-key"), CancellationToken.None);
        var secondResend = await service.ResendAsync(placed.Id,
            new ResendNotificationRequest("same-key"), CancellationToken.None);

        Assert.AreEqual(firstResend.NotificationId, secondResend.NotificationId);
        Assert.AreEqual(2, provider.SendCount);

        provider.NewMessageStatus = "queued";
        await service.DispatchOrderAsync(order.OrderId, CancellationToken.None);
        var followUp = await db.OrderNotifications.SingleAsync(x =>
            x.OrderId == order.OrderId && x.Kind == NotificationKind.DeliveryFollowUp);
        Assert.IsTrue(followUp.ScheduledFor > DateTimeOffset.UtcNow.AddDays(2));

        await service.CancelOrderAsync(order.OrderId, CancellationToken.None);
        Assert.AreEqual("canceled", followUp.ProviderStatus);
        Assert.IsTrue(provider.CancelledSids.Contains(followUp.ProviderSid!));

        await service.DisposeContentAsync(placed.Id, CancellationToken.None);
        Assert.IsNull(placed.Body);
        Assert.IsTrue(provider.RedactedSids.Contains(placed.ProviderSid!));

        await service.DeleteContactNumberAsync("shopper", contact.ContactNumberId,
            CancellationToken.None);
        Assert.IsFalse(await db.ContactNumbers.AnyAsync());
    }

    [TestMethod]
    public async Task AProviderSendFailureNeverRollsBackTheOrder()
    {
        await using var db = NewContext();
        db.CatalogItems.Add(new CatalogItem(1, 1, "description", "item", 12m, "picture"));
        await db.SaveChangesAsync();
        var provider = new FakeTwilioClient { ThrowOnSend = true };
        var service = new OrderNotificationService(db, provider, TimeProvider.System);
        await service.RegisterContactNumberAsync("shopper",
            new RegisterContactNumberRequest("test-destination"), CancellationToken.None);

        var response = await service.PlaceOrderAsync("shopper",
            new PlaceOrderRequest(new[] { new PlaceOrderItemRequest(1, 1) }),
            CancellationToken.None);

        Assert.IsTrue(await db.Orders.AnyAsync(x => x.Id == response.OrderId));
        Assert.AreEqual("failed", (await db.OrderNotifications.SingleAsync()).ProviderStatus);
    }

    private static CatalogContext NewContext() => new(new DbContextOptionsBuilder<CatalogContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private sealed class FakeTwilioClient : ITwilioMessagingClient
    {
        private readonly Dictionary<string, ProviderMessage> _messages = new();
        private int _sequence;

        public string NewMessageStatus { get; set; } = "queued";
        public bool ThrowOnSend { get; set; }
        public int SendCount { get; private set; }
        public HashSet<string> CancelledSids { get; } = new();
        public HashSet<string> RedactedSids { get; } = new();

        public Task<ValidatedPhoneNumber> ValidatePhoneNumberAsync(string phoneNumber,
            string? countryCode, CancellationToken cancellationToken) =>
            Task.FromResult(new ValidatedPhoneNumber(true, "test-destination", Array.Empty<string>()));

        public Task<ProviderMessage> SendMessageAsync(string to, string body,
            DateTimeOffset? sendAt, CancellationToken cancellationToken)
        {
            SendCount++;
            if (ThrowOnSend)
            {
                throw new TwilioRequestException(System.Net.HttpStatusCode.BadRequest, 21211);
            }

            var sid = $"SM{++_sequence:00000000000000000000000000000000}";
            var status = sendAt.HasValue ? "scheduled" : NewMessageStatus;
            var message = new ProviderMessage(sid, status, "sender", to, body, null,
                DateTimeOffset.UtcNow, sendAt.HasValue ? null : DateTimeOffset.UtcNow);
            _messages[sid] = message;
            return Task.FromResult(message);
        }

        public Task<ProviderMessage> FetchMessageAsync(string sid,
            CancellationToken cancellationToken) => Task.FromResult(_messages[sid]);

        public Task<ProviderMessage> CancelMessageAsync(string sid,
            CancellationToken cancellationToken)
        {
            CancelledSids.Add(sid);
            _messages[sid] = _messages[sid] with { Status = "canceled" };
            return Task.FromResult(_messages[sid]);
        }

        public Task<ProviderMessage> RedactMessageAsync(string sid,
            CancellationToken cancellationToken)
        {
            RedactedSids.Add(sid);
            _messages[sid] = _messages[sid] with { Body = string.Empty };
            return Task.FromResult(_messages[sid]);
        }

        public Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from,
            DateTimeOffset to, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProviderMessage>>(_messages.Values.ToList());
    }
}
