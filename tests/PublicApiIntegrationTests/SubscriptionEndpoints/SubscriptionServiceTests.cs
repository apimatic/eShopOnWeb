using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionServiceTests
{
    [TestMethod]
    public async Task ConcurrentSubscribeRequestsCreateOneCustomerAndSubscription()
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AppIdentityDbContext(options);
        var user = new ApplicationUser
        {
            Id = "test-user",
            UserName = "shopper@example.com",
            Email = "shopper@example.com"
        };
        var maxio = new FakeMaxioBillingClient();
        var service = new SubscriptionService(maxio, db);

        var results = await Task.WhenAll(
            service.SubscribeAsync(user, "eshop-pro", CancellationToken.None),
            service.SubscribeAsync(user, "eshop-pro", CancellationToken.None));

        Assert.AreEqual(1, maxio.CreateCustomerCalls);
        Assert.AreEqual(1, maxio.CreateSubscriptionCalls);
        Assert.IsTrue(results[0].Created ^ results[1].Created);
        Assert.AreEqual(results[0].Subscription.Id, results[1].Subscription.Id);
        Assert.AreEqual(1, await db.MaxioSubscriptionMappings.CountAsync());
    }

    private sealed class FakeMaxioBillingClient : IMaxioBillingClient
    {
        private MaxioCustomerRecord? _customer;
        private MaxioSubscriptionRecord? _subscription;

        public int CreateCustomerCalls { get; private set; }
        public int CreateSubscriptionCalls { get; private set; }

        public Task<IReadOnlyList<MaxioPlan>> GetPlansAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MaxioPlan>>(new[]
            {
                new MaxioPlan("eshop-pro", "Pro Plan", null, 29900, 1, "month")
            });

        public Task<MaxioCustomerRecord?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken) =>
            Task.FromResult(_customer);

        public Task<MaxioCustomerRecord> CreateCustomerAsync(string reference, string firstName, string lastName, string email, CancellationToken cancellationToken)
        {
            CreateCustomerCalls++;
            _customer = new MaxioCustomerRecord(7, reference, email);
            return Task.FromResult(_customer);
        }

        public Task<string> GetNoPaymentCollectionMethodAsync(CancellationToken cancellationToken) =>
            Task.FromResult("invoice");

        public Task<MaxioSubscriptionRecord?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken) =>
            Task.FromResult(_subscription);

        public Task<MaxioSubscriptionRecord> CreateSubscriptionAsync(string customerReference, string subscriptionReference, string productHandle, string paymentCollectionMethod, DateTimeOffset nextBillingAt, CancellationToken cancellationToken)
        {
            CreateSubscriptionCalls++;
            _subscription = new MaxioSubscriptionRecord(8, subscriptionReference, "active", productHandle, "Pro Plan", 29900, nextBillingAt.AddMonths(-1), nextBillingAt);
            return Task.FromResult(_subscription);
        }

        public Task<IReadOnlyList<MaxioSubscriptionRecord>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MaxioSubscriptionRecord>>(new[] { _subscription! });
    }
}
