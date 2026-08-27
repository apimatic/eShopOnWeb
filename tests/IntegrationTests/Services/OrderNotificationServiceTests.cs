using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Services;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Services;

public class OrderNotificationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PlaceOrderSurvivesProviderFailureAndRecordsIt()
    {
        await using var context = CreateContext();
        await SeedCatalogAndContactAsync(context);
        var provider = new FakeProvider { ThrowOnSend = true };
        var service = CreateService(context, provider);

        var order = await service.PlaceOrderAsync(
            "shopper",
            new[] { new OrderLineInput(1, 2) },
            Address(),
            default);

        Assert.True(order.Id > 0);
        var notification = await context.OrderNotifications.SingleAsync();
        Assert.Equal("submission_failed", notification.ProviderStatus);
    }

    [Fact]
    public async Task CancelOrderCancelsProviderScheduledFollowUp()
    {
        await using var context = CreateContext();
        await SeedCatalogAndContactAsync(context);
        var provider = new FakeProvider();
        var service = CreateService(context, provider);
        var order = await service.PlaceOrderAsync(
            "shopper",
            new[] { new OrderLineInput(1, 1) },
            Address(),
            default);

        await service.DispatchOrderAsync(order.Id, default);
        var followUp = await context.OrderNotifications.SingleAsync(x => x.Kind == NotificationKind.DeliveryFollowUp);
        Assert.Equal(Now.Add(OrderNotificationService.FollowUpDelay), followUp.ScheduledFor);
        Assert.Equal("scheduled", followUp.ProviderStatus);

        await service.CancelOrderAsync(order.Id, default);

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Single(provider.CancelledSids);
        Assert.Equal("canceled", followUp.ProviderStatus);
    }

    [Fact]
    public async Task ResendWithSameIdempotencyKeySendsOnlyOnce()
    {
        await using var context = CreateContext();
        await SeedCatalogAndContactAsync(context);
        var provider = new FakeProvider { ImmediateStatus = "undelivered" };
        var service = CreateService(context, provider);
        var order = await service.PlaceOrderAsync(
            "shopper",
            new[] { new OrderLineInput(1, 1) },
            Address(),
            default);
        var original = await context.OrderNotifications.SingleAsync();

        var first = await service.ResendAsync(original.Id, "attempt-one", default);
        var repeated = await service.ResendAsync(original.Id, "attempt-one", default);

        Assert.Equal(first.Id, repeated.Id);
        Assert.Equal(2, provider.SendCount);
        Assert.Equal(2, await context.OrderNotifications.CountAsync());
    }

    [Fact]
    public async Task RemovingContactCancelsItsScheduledFollowUp()
    {
        await using var context = CreateContext();
        var contact = await SeedCatalogAndContactAsync(context);
        var provider = new FakeProvider();
        var service = CreateService(context, provider);
        var order = await service.PlaceOrderAsync(
            "shopper",
            new[] { new OrderLineInput(1, 1) },
            Address(),
            default);
        await service.DispatchOrderAsync(order.Id, default);

        await service.DeleteContactNumberAsync("shopper", contact.Id, default);

        Assert.Empty(await context.ContactNumbers.ToListAsync());
        Assert.Single(provider.CancelledSids);
    }

    private static CatalogContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new CatalogContext(options);
    }

    private static async Task<ContactNumber> SeedCatalogAndContactAsync(CatalogContext context)
    {
        context.CatalogItems.Add(new CatalogItem(1, 1, "description", "item", 10m, "picture"));
        var contact = new ContactNumber("shopper", "+15555550100", Now);
        context.ContactNumbers.Add(contact);
        await context.SaveChangesAsync();
        return contact;
    }

    private static OrderNotificationService CreateService(CatalogContext context, FakeProvider provider) =>
        new(context, provider, new FixedTimeProvider(Now));

    private static Address Address() => new("street", "city", "state", "country", "zip");

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;
        public FixedTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    private sealed class FakeProvider : IOrderMessagingProvider
    {
        private readonly Dictionary<string, ProviderMessageState> _messages = new();
        private int _nextSid;

        public bool ThrowOnSend { get; set; }
        public string ImmediateStatus { get; set; } = "delivered";
        public int SendCount { get; private set; }
        public List<string> CancelledSids { get; } = new();

        public Task<PhoneNumberValidation> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken) =>
            Task.FromResult(new PhoneNumberValidation(true, phoneNumber));

        public Task<ProviderMessageState> SendAsync(string to, string body, CancellationToken cancellationToken)
        {
            SendCount++;
            if (ThrowOnSend)
            {
                throw new MessagingProviderException("send");
            }

            return Task.FromResult(Add(ImmediateStatus));
        }

        public Task<ProviderMessageState> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken) =>
            Task.FromResult(Add("scheduled"));

        public Task<ProviderMessageState> GetAsync(string messageSid, CancellationToken cancellationToken) =>
            Task.FromResult(_messages[messageSid]);

        public Task<ProviderMessageState> CancelAsync(string messageSid, CancellationToken cancellationToken)
        {
            CancelledSids.Add(messageSid);
            var state = new ProviderMessageState(messageSid, "canceled", null, null);
            _messages[messageSid] = state;
            return Task.FromResult(state);
        }

        public Task<ProviderMessageState> RedactContentAsync(string messageSid, CancellationToken cancellationToken) =>
            Task.FromResult(_messages[messageSid]);

        public Task<IReadOnlyList<ProviderMessageRecord>> ListFromApplicationNumberAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProviderMessageRecord>>(Array.Empty<ProviderMessageRecord>());

        private ProviderMessageState Add(string status)
        {
            var sid = $"SM{++_nextSid:D32}";
            var state = new ProviderMessageState(sid, status, null, null);
            _messages[sid] = state;
            return state;
        }
    }
}
