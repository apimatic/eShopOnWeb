using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionBillingServiceTests
{
    [TestMethod]
    public async Task ConcurrentDoubleClickCreatesOneCustomerAndOneSubscription()
    {
        var maxio = new ConcurrentFakeMaxioClient();
        var shopper = new FakeShopperIdentityService();
        var service = new SubscriptionBillingService(
            maxio,
            shopper,
            Options.Create(new MaxioOptions { ProductFamilyHandle = "family" }),
            new AsyncKeyedLocker());

        var first = service.SubscribeAsync("demo@example.com", "eshop-pro", CancellationToken.None);
        var second = service.SubscribeAsync("demo@example.com", "eshop-pro", CancellationToken.None);
        var results = await Task.WhenAll(first, second);

        Assert.AreEqual(1, maxio.CreateCustomerCalls);
        Assert.AreEqual(1, maxio.CreateSubscriptionCalls);
        Assert.AreEqual(1, Array.FindAll(results, result => result.AlreadyExisted).Length);
        Assert.AreEqual(results[0].Subscription.Id, results[1].Subscription.Id);
    }

    private sealed class FakeShopperIdentityService : IShopperIdentityService
    {
        public Task<ShopperIdentity?> FindByNameAsync(string userName) =>
            Task.FromResult<ShopperIdentity?>(new ShopperIdentity("user-id", "demo@example.com"));
    }

    private sealed class ConcurrentFakeMaxioClient : IMaxioClient
    {
        private readonly MaxioProduct _product = new()
        {
            Id = 7,
            Name = "Pro",
            Handle = "eshop-pro",
            PriceInCents = 29900,
            Interval = 1,
            IntervalUnit = "month",
            ProductFamily = new MaxioProductFamily { Id = 3, Name = "Plans", Handle = "family" }
        };
        private MaxioCustomer? _customer;
        private MaxioSubscription? _subscription;
        private int _createCustomerCalls;
        private int _createSubscriptionCalls;

        internal int CreateCustomerCalls => _createCustomerCalls;
        internal int CreateSubscriptionCalls => _createSubscriptionCalls;

        public Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new MaxioSite { Currency = "USD", Test = true });

        public Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(
            string familyHandle,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MaxioProduct>>(new[] { _product });

        public Task<MaxioCustomer?> FindCustomerByReferenceAsync(
            string reference,
            CancellationToken cancellationToken) => Task.FromResult(_customer);

        public async Task<MaxioCustomer> CreateCustomerAsync(
            MaxioCreateCustomer customer,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _createCustomerCalls);
            await Task.Delay(30, cancellationToken);
            _customer = new MaxioCustomer
            {
                Id = 5,
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
                Reference = customer.Reference
            };
            return _customer;
        }

        public Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(
            string reference,
            CancellationToken cancellationToken) => Task.FromResult(_subscription);

        public async Task<MaxioSubscription> CreateSubscriptionAsync(
            MaxioCreateSubscription subscription,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _createSubscriptionCalls);
            await Task.Delay(30, cancellationToken);
            _subscription = new MaxioSubscription
            {
                Id = 11,
                State = "active",
                ProductPriceInCents = _product.PriceInCents,
                CreatedAt = DateTimeOffset.UtcNow,
                Reference = subscription.Reference,
                Currency = "USD",
                Customer = _customer!,
                Product = _product
            };
            return _subscription;
        }

        public Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
            int customerId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MaxioSubscription>>(
                _subscription is null ? Array.Empty<MaxioSubscription>() : new[] { _subscription });
    }
}
