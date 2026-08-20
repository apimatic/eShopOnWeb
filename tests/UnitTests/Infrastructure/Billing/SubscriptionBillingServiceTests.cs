using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class SubscriptionBillingServiceTests
{
    [Fact]
    public async Task ConcurrentDoubleClickCreatesOnlyOneMaxioSubscription()
    {
        var databaseName = $"subscription-test-{Guid.NewGuid()}";
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        const string userId = "user-123";

        await using (var seedContext = new AppIdentityDbContext(options))
        {
            seedContext.Users.Add(new ApplicationUser
            {
                Id = userId,
                UserName = "shopper@example.test",
                Email = "shopper@example.test",
                FirstName = "Test",
                LastName = "Shopper"
            });
            await seedContext.SaveChangesAsync();
        }

        var gateway = new RecordingGateway();
        var keyedLock = new AsyncKeyedLock();
        await using var firstContext = new AppIdentityDbContext(options);
        await using var secondContext = new AppIdentityDbContext(options);
        var firstService = new SubscriptionBillingService(firstContext, gateway, keyedLock);
        var secondService = new SubscriptionBillingService(secondContext, gateway, keyedLock);

        var results = await Task.WhenAll(
            firstService.SubscribeAsync(userId, "eshop-pro", CancellationToken.None),
            secondService.SubscribeAsync(userId, "eshop-pro", CancellationToken.None));

        Assert.Equal(1, gateway.CreateCalls);
        Assert.Single(results, result => result.Created);
        Assert.Single(results, result => !result.Created);

        await using var assertContext = new AppIdentityDbContext(options);
        Assert.Single(await assertContext.SubscriptionEnrollments.ToListAsync());
    }

    private sealed class RecordingGateway : IMaxioBillingGateway
    {
        private readonly ConcurrentDictionary<string, SubscriptionDetails> _subscriptions = new(StringComparer.Ordinal);
        private int _createCalls;

        public int CreateCalls => _createCalls;

        public Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SubscriptionPlan>>([Plan]);

        public Task<SubscriptionPlan> GetPlanAsync(string productHandle, CancellationToken cancellationToken) =>
            Task.FromResult(Plan);

        public Task EnsureCustomerAsync(BillingCustomerProfile profile, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<SubscriptionDetails?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
        {
            _subscriptions.TryGetValue(reference, out var subscription);
            return Task.FromResult(subscription);
        }

        public async Task<SubscriptionDetails> CreateSubscriptionAsync(
            string productHandle,
            string customerReference,
            string subscriptionReference,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _createCalls);
            await Task.Delay(50, cancellationToken);
            var subscription = new SubscriptionDetails(
                42,
                subscriptionReference,
                Plan.Handle,
                Plan.Name,
                Plan.PriceInCents,
                "USD",
                "active",
                DateTimeOffset.UtcNow.AddMonths(1),
                Plan.Interval,
                Plan.IntervalUnit);
            _subscriptions[subscriptionReference] = subscription;
            return subscription;
        }

        public Task<IReadOnlyList<SubscriptionDetails>> GetSubscriptionsAsync(string customerReference, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SubscriptionDetails>>(_subscriptions.Values.ToArray());

        private static SubscriptionPlan Plan => new("eshop-pro", "Pro Plan", "Test plan", 29900, 1, "month");
    }
}
