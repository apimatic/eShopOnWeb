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
using Microsoft.eShopWeb.PublicApi.OrderNotifications;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.OrderNotifications;

[TestClass]
public class OrderNotificationServiceTests
{
    [TestMethod]
    public async Task DispatchThenCancel_CancelsProviderScheduledFollowUp()
    {
        await using var db = NewContext();
        var catalogItem = await SeedCatalogItemAsync(db);
        db.ContactNumbers.Add(new ContactNumber("shopper@example.com", "+14165550100", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        var provider = new FakeSmsProvider();
        var service = new OrderNotificationService(db, provider);

        var order = await service.PlaceOrderAsync(
            "shopper@example.com",
            new[] { new OrderLineInput(catalogItem.Id, 2) },
            Address(),
            CancellationToken.None);
        await service.DispatchOrderAsync(order.Id, CancellationToken.None);

        var scheduled = provider.Messages.Values.Single(x => x.Status == "scheduled");
        Assert.IsNotNull(scheduled.DateCreated);

        var cancelled = await service.CancelOrderAsync(order.Id, CancellationToken.None);

        Assert.AreEqual(OrderStatus.Cancelled, cancelled!.Status);
        Assert.AreEqual("canceled", provider.Messages[scheduled.Sid].Status);
        Assert.AreEqual(4, provider.Messages.Count);
        var records = await db.OrderNotifications.Where(x => x.OrderId == order.Id).ToListAsync();
        Assert.AreEqual("canceled", records.Single(x => x.Kind == NotificationKind.DeliveryFollowUp).ProviderStatus);
    }

    [TestMethod]
    public async Task ProviderSendFailure_DoesNotRollBackOrder()
    {
        await using var db = NewContext();
        var catalogItem = await SeedCatalogItemAsync(db);
        db.ContactNumbers.Add(new ContactNumber("shopper@example.com", "+14165550100", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        var provider = new FakeSmsProvider { RejectSends = true };
        var service = new OrderNotificationService(db, provider);

        var order = await service.PlaceOrderAsync(
            "shopper@example.com",
            new[] { new OrderLineInput(catalogItem.Id, 1) },
            Address(),
            CancellationToken.None);

        Assert.IsTrue(await db.Orders.AnyAsync(x => x.Id == order.Id));
        var notification = await db.OrderNotifications.SingleAsync(x => x.OrderId == order.Id);
        Assert.AreEqual(NotificationDeliveryStatus.ProviderRejected, notification.ProviderStatus);
        Assert.AreEqual(21211, notification.ProviderErrorCode);
    }

    [TestMethod]
    public async Task Resend_ReusesIdempotencyKeyButAllowsFreshKey()
    {
        await using var db = NewContext();
        var catalogItem = await SeedCatalogItemAsync(db);
        var contact = new ContactNumber("shopper@example.com", "+14165550100", DateTimeOffset.UtcNow);
        db.ContactNumbers.Add(contact);
        var order = new Order(
            "shopper@example.com",
            new Address("1 Main St", "Toronto", "ON", "CA", "M5V 1A1"),
            new List<OrderItem> { new(new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri), catalogItem.Price, 1) });
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        var source = new OrderNotification(
            order.Id,
            order.BuyerId,
            contact.Id,
            contact.Value,
            NotificationKind.OrderPlaced,
            "Order placed",
            DateTimeOffset.UtcNow);
        source.RecordProviderResult("SMFAILED", "undelivered", 30005, null, DateTimeOffset.UtcNow);
        db.OrderNotifications.Add(source);
        await db.SaveChangesAsync();
        var provider = new FakeSmsProvider();
        provider.Messages["SMFAILED"] = new ProviderMessage("SMFAILED", "undelivered", "Order placed", "+15005550000", contact.Value, 30005, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var service = new OrderNotificationService(db, provider);

        var first = await service.ResendAsync(source.Id, "retry-1", CancellationToken.None);
        var repeat = await service.ResendAsync(source.Id, "retry-1", CancellationToken.None);
        var second = await service.ResendAsync(source.Id, "retry-2", CancellationToken.None);

        Assert.IsTrue(first!.WasCreated);
        Assert.IsFalse(repeat!.WasCreated);
        Assert.AreEqual(first.Notification.Id, repeat.Notification.Id);
        Assert.AreNotEqual(first.Notification.Id, second!.Notification.Id);
        Assert.AreEqual(2, provider.SendCount);
    }

    [TestMethod]
    public async Task ContentDisposal_RedactsProviderAndKeepsAuditRecord()
    {
        await using var db = NewContext();
        var notification = new OrderNotification(12, "shopper@example.com", 4, "+14165550100", NotificationKind.OrderPlaced, "Sensitive body", DateTimeOffset.UtcNow);
        notification.RecordProviderResult("SMREDACT", "delivered", null, null, DateTimeOffset.UtcNow);
        db.OrderNotifications.Add(notification);
        await db.SaveChangesAsync();
        var provider = new FakeSmsProvider();
        provider.Messages["SMREDACT"] = new ProviderMessage("SMREDACT", "delivered", "Sensitive body", "+15005550000", "+14165550100", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var service = new OrderNotificationService(db, provider);

        Assert.IsTrue(await service.DeleteContentAsync(notification.Id, CancellationToken.None));

        Assert.IsNull(notification.Body);
        Assert.IsNotNull(notification.ContentDeletedAt);
        Assert.AreEqual(string.Empty, provider.Messages["SMREDACT"].Body);
        Assert.AreEqual("delivered", notification.ProviderStatus);
    }

    private static CatalogContext NewContext()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new CatalogContext(options);
    }

    private static async Task<CatalogItem> SeedCatalogItemAsync(CatalogContext db)
    {
        var item = new CatalogItem(1, 1, "Description", "Test item", 10m, "image.png");
        db.CatalogItems.Add(item);
        await db.SaveChangesAsync();
        return item;
    }

    private static AddressInput Address() => new("1 Main St", "Toronto", "ON", "CA", "M5V 1A1");

    private sealed class FakeSmsProvider : ISmsProvider
    {
        private int _sequence;
        public bool RejectSends { get; set; }
        public int SendCount { get; private set; }
        public Dictionary<string, ProviderMessage> Messages { get; } = new(StringComparer.Ordinal);

        public Task<PhoneNumberValidation> ValidateDestinationAsync(string rawNumber, string? countryCode, CancellationToken cancellationToken)
            => Task.FromResult(new PhoneNumberValidation(true, rawNumber, Array.Empty<string>()));

        public Task<ProviderMessage> SendAsync(string destination, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken)
        {
            SendCount++;
            if (RejectSends)
            {
                throw new SmsProviderException("Rejected", 21211);
            }
            var sid = $"SM{++_sequence:000000}";
            var message = new ProviderMessage(
                sid,
                sendAt.HasValue ? "scheduled" : "queued",
                body,
                "+15005550000",
                destination,
                null,
                DateTimeOffset.UtcNow,
                sendAt.HasValue ? null : DateTimeOffset.UtcNow);
            Messages[sid] = message;
            return Task.FromResult(message);
        }

        public Task<ProviderMessage> GetMessageAsync(string providerMessageSid, CancellationToken cancellationToken)
            => Task.FromResult(Messages[providerMessageSid]);

        public Task<ProviderMessage> CancelMessageAsync(string providerMessageSid, CancellationToken cancellationToken)
        {
            var message = Messages[providerMessageSid] with { Status = "canceled" };
            Messages[providerMessageSid] = message;
            return Task.FromResult(message);
        }

        public Task<ProviderMessage> RedactMessageAsync(string providerMessageSid, CancellationToken cancellationToken)
        {
            var message = Messages[providerMessageSid] with { Body = string.Empty };
            Messages[providerMessageSid] = message;
            return Task.FromResult(message);
        }

        public Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ProviderMessage>>(Messages.Values
                .Where(x => (x.DateSent ?? x.DateCreated) >= from && (x.DateSent ?? x.DateCreated) <= to)
                .ToList());
    }
}
