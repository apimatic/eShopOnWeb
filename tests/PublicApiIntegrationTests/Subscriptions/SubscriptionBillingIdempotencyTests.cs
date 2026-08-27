using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Subscriptions;

[TestClass]
public class SubscriptionBillingIdempotencyTests
{
    [TestMethod]
    public async Task ConcurrentDoubleClickCreatesOneProviderSubscription()
    {
        var root = new InMemoryDatabaseRoot();
        var databaseName = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase(databaseName, root)
            .Options;
        var user = new BillingUser("user-1", "shopper@example.test", "Shopper", "Customer");
        await using (var seed = new AppIdentityDbContext(options))
        {
            seed.Users.Add(new ApplicationUser { Id = user.Id, UserName = user.Email, Email = user.Email });
            await seed.SaveChangesAsync();
        }

        var gateway = new FakeGateway();
        var keyLock = new SubscriptionKeyLock();
        await using var context1 = new AppIdentityDbContext(options);
        await using var context2 = new AppIdentityDbContext(options);
        var service1 = new SubscriptionBillingService(context1, gateway, keyLock);
        var service2 = new SubscriptionBillingService(context2, gateway, keyLock);

        var results = await Task.WhenAll(
            service1.SubscribeAsync(user, "eshop-pro", CancellationToken.None),
            service2.SubscribeAsync(user, "eshop-pro", CancellationToken.None));

        Assert.AreEqual(1, gateway.CreateCount);
        Assert.IsTrue(results.All(result => result.Id == 77));
        await using var verification = new AppIdentityDbContext(options);
        Assert.AreEqual(1, await verification.MaxioSubscriptionLinks.CountAsync());
        Assert.AreEqual(MaxioSubscriptionLinkStatus.Succeeded,
            (await verification.MaxioSubscriptionLinks.SingleAsync()).Status);
    }

    private sealed class FakeGateway : IMaxioGateway
    {
        private SubscriptionDto? _subscription;
        private int _createCount;
        public int CreateCount => _createCount;

        public Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SubscriptionPlanDto>>([Plan]);

        public Task<SubscriptionPlanDto> GetPlanAsync(string productHandle, CancellationToken cancellationToken) =>
            Task.FromResult(Plan);

        public Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken) =>
            Task.FromResult<MaxioCustomer?>(new MaxioCustomer(10, reference));

        public Task<MaxioCustomer> EnsureCustomerAsync(BillingUser user, CancellationToken cancellationToken) =>
            Task.FromResult(new MaxioCustomer(10, user.Id));

        public Task<SubscriptionDto?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken) =>
            Task.FromResult(_subscription);

        public Task<IReadOnlyList<SubscriptionDto>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SubscriptionDto>>(_subscription is null ? [] : [_subscription]);

        public async Task<SubscriptionDto> CreateSubscriptionAsync(
            string customerReference,
            string productHandle,
            string subscriptionReference,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _createCount);
            await Task.Delay(100, cancellationToken);
            _subscription = new SubscriptionDto(77, "eshop-pro", "Pro", 29900, "active", DateTimeOffset.UtcNow.AddMonths(1));
            return _subscription;
        }

        private static readonly SubscriptionPlanDto Plan = new("eshop-pro", "Pro", null, 29900, 1, "month");
    }
}
