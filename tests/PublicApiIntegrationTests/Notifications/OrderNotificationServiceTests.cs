using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Notifications;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Notifications;

[TestClass]
public class OrderNotificationServiceTests
{
    private const string Shopper = "shopper@example.test";
    private const string Destination = "+10000000000";

    [TestMethod]
    public async Task SendFailureDoesNotRollBackPlacedOrder()
    {
        await using var db = CreateContext();
        db.CatalogItems.Add(new CatalogItem(1, 1, "description", "item", 12m, "picture.png"));
        await db.SaveChangesAsync();
        var provider = new FakeGateway { FailSends = true };
        var service = CreateService(db, provider);
        await service.RegisterContactNumberAsync(Shopper, new RegisterContactNumberRequest(Destination), default);

        var response = await service.PlaceOrderAsync(Shopper, ValidOrderRequest(), default);

        Assert.IsTrue(response.OrderId > 0);
        Assert.AreEqual(1, await db.Orders.CountAsync());
        var notification = await db.OrderNotifications.SingleAsync();
        Assert.AreEqual("unknown_outcome", notification.Outcome);
    }

    [TestMethod]
    public async Task ContactNumbersAreShopperScoped()
    {
        await using var db = CreateContext();
        var service = CreateService(db, new FakeGateway());
        var registered = await service.RegisterContactNumberAsync(
            Shopper, new RegisterContactNumberRequest(Destination), default);

        Assert.AreEqual(0, (await service.GetContactNumbersAsync("other@example.test", default)).Count);
        var ex = await Assert.ThrowsExceptionAsync<NotificationApiException>(() =>
            service.DeleteContactNumberAsync("other@example.test", registered.ContactNumberId, default));
        Assert.AreEqual(404, ex.StatusCode);
        Assert.AreEqual(1, (await service.GetContactNumbersAsync(Shopper, default)).Count);
    }

    [TestMethod]
    public async Task RepeatingResendKeyReturnsSameNotificationWithoutSecondSend()
    {
        await using var db = CreateContext();
        var contact = new ContactNumber(Shopper, Destination);
        db.ContactNumbers.Add(contact);
        var order = ExistingOrder();
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        var original = new OrderNotification(
            order.Id, contact.Id, Shopper, NotificationKind.OrderPlaced, "message");
        original.RecordFailure("send_failed");
        db.OrderNotifications.Add(original);
        await db.SaveChangesAsync();
        var provider = new FakeGateway();
        var service = CreateService(db, provider);

        var first = await service.ResendAsync(original.Id, "same-key", default);
        var repeated = await service.ResendAsync(original.Id, "same-key", default);
        var freshAttempt = await service.ResendAsync(original.Id, "fresh-key", default);

        Assert.AreEqual(first.NotificationId, repeated.NotificationId);
        Assert.AreNotEqual(first.NotificationId, freshAttempt.NotificationId);
        Assert.AreEqual(2, provider.SendCount);
    }

    [TestMethod]
    public async Task DeletingContactCancelsItsQueuedProviderMessage()
    {
        await using var db = CreateContext();
        var contact = new ContactNumber(Shopper, Destination);
        db.ContactNumbers.Add(contact);
        var order = ExistingOrder();
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        var notification = new OrderNotification(
            order.Id,
            contact.Id,
            Shopper,
            NotificationKind.DeliveryFollowUp,
            "follow up",
            DateTimeOffset.UtcNow.AddDays(3));
        notification.RecordProviderState("SM-scheduled", "scheduled", null, DateTimeOffset.UtcNow, null);
        db.OrderNotifications.Add(notification);
        await db.SaveChangesAsync();
        var provider = new FakeGateway();
        var service = CreateService(db, provider);

        await service.DeleteContactNumberAsync(Shopper, contact.Id, default);

        Assert.AreEqual(1, provider.CancelCount);
        Assert.AreEqual(0, (await service.GetContactNumbersAsync(Shopper, default)).Count);
        Assert.AreEqual("canceled", notification.ProviderStatus);
    }

    private static CatalogContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new CatalogContext(options);
    }

    private static OrderNotificationService CreateService(CatalogContext db, FakeGateway provider) =>
        new(db, provider, Options.Create(new TwilioSettings
        {
            AccountSid = "test-account",
            AuthToken = "test-token",
            FromNumber = Destination,
            MessagingServiceSid = "test-service"
        }));

    private static PlaceOrderRequest ValidOrderRequest() => new(
        new[] { new OrderLineRequest(1, 1) },
        new ShippingAddressRequest("street", "city", "state", "country", "zip"));

    private static Order ExistingOrder() => new(
        Shopper,
        new Address("street", "city", "state", "country", "zip"),
        new List<OrderItem>());

    private sealed class FakeGateway : ITwilioMessagingGateway
    {
        public bool FailSends { get; init; }
        public int SendCount { get; private set; }
        public int CancelCount { get; private set; }

        public Task<ProviderPhoneValidation> ValidatePhoneNumberAsync(string submittedNumber, CancellationToken cancellationToken) =>
            Task.FromResult(new ProviderPhoneValidation(true, Destination));

        public Task<ProviderMessage> SendImmediateAsync(string canonicalDestination, string body, CancellationToken cancellationToken)
        {
            SendCount++;
            if (FailSends)
            {
                throw new MessagingProviderException("failure");
            }
            return Task.FromResult(Message($"SM-{SendCount}", "queued", body));
        }

        public Task<ProviderMessage> ScheduleAsync(string canonicalDestination, string body, DateTimeOffset sendAt, CancellationToken cancellationToken) =>
            Task.FromResult(Message("SM-scheduled", "scheduled", body));

        public Task<ProviderMessage> FetchAsync(string providerSid, CancellationToken cancellationToken) =>
            Task.FromResult(Message(providerSid, "undelivered", "message"));

        public Task<ProviderMessage> CancelAsync(string providerSid, CancellationToken cancellationToken)
        {
            CancelCount++;
            return Task.FromResult(Message(providerSid, "canceled", "message"));
        }

        public Task<ProviderMessage> RedactAsync(string providerSid, CancellationToken cancellationToken) =>
            Task.FromResult(Message(providerSid, "delivered", string.Empty));

        public Task<ProviderMessagePage> ListAsync(DateTimeOffset widenedLower, DateTimeOffset widenedUpper, string? pageToken, CancellationToken cancellationToken) =>
            Task.FromResult(new ProviderMessagePage(Array.Empty<ProviderMessage>(), null));

        private static ProviderMessage Message(string sid, string status, string? body) =>
            new(sid, status, null, body, Destination, DateTimeOffset.UtcNow, null);
    }
}
