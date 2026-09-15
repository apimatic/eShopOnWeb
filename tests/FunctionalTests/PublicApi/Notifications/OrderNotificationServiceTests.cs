using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Notifications;
using Xunit;

namespace Microsoft.eShopWeb.FunctionalTests.PublicApi.Notifications;

public class OrderNotificationServiceTests
{
    [Fact]
    public async Task DispatchQueuesFollowUpAndCancelCancelsIt()
    {
        await using var db = CreateContext();
        var provider = new FakeTwilioClient();
        var service = new OrderNotificationService(db, provider, TimeProvider.System);
        await service.RegisterContactNumberAsync("shopper", new RegisterContactNumberRequest("input"), default);
        db.CatalogItems.Add(new CatalogItem(1, 1, "description", "product", 12.50m, "picture"));
        await db.SaveChangesAsync();

        var created = await service.CreateOrderAsync("shopper", OrderRequest(), default);
        await service.DispatchOrderAsync(created.OrderId, default);
        var scheduled = await db.OrderNotifications.SingleAsync(x => x.Kind == NotificationKind.DeliveryFollowUp);

        Assert.Equal("scheduled", scheduled.ProviderStatus);
        Assert.NotNull(scheduled.ScheduledFor);
        await service.CancelOrderAsync(created.OrderId, default);
        Assert.Equal("canceled", scheduled.ProviderStatus);
        Assert.False(scheduled.CancellationPending);
        Assert.Contains(provider.Messages.Values, x => x.Status == "canceled");
    }

    [Fact]
    public async Task ResendWithSameIdempotencyKeySendsOnlyOnce()
    {
        await using var db = CreateContext();
        var provider = new FakeTwilioClient();
        var service = new OrderNotificationService(db, provider, TimeProvider.System);
        var contact = new ContactNumber("shopper", FakeTwilioClient.CanonicalNumber, DateTimeOffset.UtcNow);
        db.ContactNumbers.Add(contact);
        var order = new Order("shopper", new Address("street", "city", "state", "country", "zip"), new List<OrderItem>());
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        var failed = new OrderNotification(order.Id, "shopper", contact.Id, NotificationKind.OrderPlaced,
            "content", DateTimeOffset.UtcNow);
        failed.RecordProviderFailure(30001);
        db.OrderNotifications.Add(failed);
        await db.SaveChangesAsync();

        var first = await service.ResendAsync(failed.Id, "same-key", default);
        var second = await service.ResendAsync(failed.Id, "same-key", default);
        var freshAttempt = await service.ResendAsync(failed.Id, "fresh-key", default);

        Assert.Equal(first.NotificationId, second.NotificationId);
        Assert.NotEqual(first.NotificationId, freshAttempt.NotificationId);
        Assert.Equal(2, provider.SendCalls.Count);
    }

    [Fact]
    public async Task ContactNumbersAreOwnerScopedAndDeletedNumbersAreNotUsed()
    {
        await using var db = CreateContext();
        var provider = new FakeTwilioClient();
        var service = new OrderNotificationService(db, provider, TimeProvider.System);
        var registered = await service.RegisterContactNumberAsync("shopper-a",
            new RegisterContactNumberRequest("input"), default);

        Assert.Empty((await service.GetContactNumbersAsync("shopper-b", default)).ContactNumbers);
        await Assert.ThrowsAsync<ApiProblemException>(() =>
            service.DeleteContactNumberAsync("shopper-b", registered.ContactNumberId, default));
        await service.DeleteContactNumberAsync("shopper-a", registered.ContactNumberId, default);
        Assert.Empty((await service.GetContactNumbersAsync("shopper-a", default)).ContactNumbers);
    }

    private static CatalogContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new CatalogContext(options);
    }

    private static CreateOrderRequest OrderRequest() => new(
        new[] { new CreateOrderItemRequest(1, 2) },
        new ShippingAddressRequest("street", "city", "state", "country", "zip"));

    private sealed class FakeTwilioClient : ITwilioClient
    {
        public const string CanonicalNumber = "+10000000000";
        private int _next;
        public Dictionary<string, TwilioMessage> Messages { get; } = new();
        public List<string> SendCalls { get; } = new();

        public Task<ValidatedPhoneNumber> ValidatePhoneNumberAsync(string input, CancellationToken cancellationToken) =>
            Task.FromResult(new ValidatedPhoneNumber(true, CanonicalNumber));

        public Task<TwilioMessage> SendMessageAsync(string to, string body, DateTimeOffset? sendAt,
            CancellationToken cancellationToken)
        {
            SendCalls.Add(body);
            var sid = $"SM{++_next:D32}";
            var value = new TwilioMessage(sid, sendAt.HasValue ? "scheduled" : "delivered", null,
                DateTimeOffset.UtcNow, sendAt.HasValue ? null : DateTimeOffset.UtcNow);
            Messages[sid] = value;
            return Task.FromResult(value);
        }

        public Task<TwilioMessage> GetMessageAsync(string sid, CancellationToken cancellationToken) =>
            Task.FromResult(Messages[sid]);

        public Task<TwilioMessage> CancelMessageAsync(string sid, CancellationToken cancellationToken)
        {
            var value = Messages[sid] with { Status = "canceled" };
            Messages[sid] = value;
            return Task.FromResult(value);
        }

        public Task<TwilioMessage> RedactMessageAsync(string sid, CancellationToken cancellationToken) =>
            Task.FromResult(Messages[sid]);

        public Task<IReadOnlyList<TwilioMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<TwilioMessage>>(Messages.Values.ToList());
    }
}
