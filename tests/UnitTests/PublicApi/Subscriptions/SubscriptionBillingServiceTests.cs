using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.PublicApi.Subscriptions;

public class SubscriptionBillingServiceTests
{
    [Fact]
    public async Task ConcurrentDoubleClickCreatesOneCustomerAndOneSubscription()
    {
        var databaseName = Guid.NewGuid().ToString();
        var databaseRoot = new InMemoryDatabaseRoot();
        await using var firstContext = CreateContext(databaseName, databaseRoot);
        await using var secondContext = CreateContext(databaseName, databaseRoot);
        var maxio = new FakeMaxioClient();
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "key",
            Subdomain = "site",
            ProductFamilyHandle = "family"
        });
        var firstService = new SubscriptionBillingService(firstContext, maxio, options, NullLogger<SubscriptionBillingService>.Instance);
        var secondService = new SubscriptionBillingService(secondContext, maxio, options, NullLogger<SubscriptionBillingService>.Instance);
        var user = new BillingUser("user-id", "shopper@example.com");

        var enrollments = await Task.WhenAll(
            firstService.SubscribeAsync(user, "pro-plan"),
            secondService.SubscribeAsync(user, "pro-plan"));

        Assert.Equal(1, maxio.CustomerCreateCount);
        Assert.Equal(1, maxio.SubscriptionCreateCount);
        Assert.Contains(enrollments, enrollment => !enrollment.AlreadyExisted);
        Assert.Contains(enrollments, enrollment => enrollment.AlreadyExisted);
        Assert.All(enrollments, enrollment => Assert.Equal(99, enrollment.Subscription.Id));
        Assert.Single(await firstContext.BillingSubscriptions.ToListAsync());
    }

    private static CatalogContext CreateContext(string databaseName, InMemoryDatabaseRoot databaseRoot)
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .Options;
        return new CatalogContext(options);
    }

    private sealed class FakeMaxioClient : IMaxioClient
    {
        private readonly ConcurrentDictionary<string, MaxioSubscription> _subscriptions = new();
        private MaxioCustomer? _customer;
        private int _customerCreateCount;
        private int _subscriptionCreateCount;

        public int CustomerCreateCount => _customerCreateCount;
        public int SubscriptionCreateCount => _subscriptionCreateCount;

        public Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MaxioProduct>>(new[]
            {
                new MaxioProduct
                {
                    Id = 42,
                    Name = "Pro",
                    Handle = "pro-plan",
                    PriceInCents = 29900,
                    Interval = 1,
                    IntervalUnit = "month",
                    ProductFamily = new MaxioProductFamily { Handle = "family" }
                }
            });

        public Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken) =>
            Task.FromResult(_customer);

        public Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
        {
            _subscriptions.TryGetValue(reference, out var subscription);
            return Task.FromResult(subscription);
        }

        public async Task<MaxioSubscription> CreateSubscriptionAsync(
            long? customerId,
            MaxioCreateCustomer? customerAttributes,
            string productHandle,
            string reference,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _subscriptionCreateCount);
            if (customerAttributes is not null)
            {
                Interlocked.Increment(ref _customerCreateCount);
            }
            await Task.Delay(50, cancellationToken);
            _customer ??= new MaxioCustomer { Id = customerId ?? 7, Reference = customerAttributes?.Reference };
            var subscription = new MaxioSubscription
            {
                Id = 99,
                State = "active",
                ProductPriceInCents = 29900,
                CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddMonths(1),
                Currency = "USD",
                Reference = reference,
                Customer = _customer,
                Product = new MaxioProduct
                {
                    Name = "Pro",
                    Handle = productHandle,
                    Interval = 1,
                    IntervalUnit = "month",
                    ProductFamily = new MaxioProductFamily { Handle = "family" }
                }
            };
            _subscriptions[reference] = subscription;
            return subscription;
        }

        public Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MaxioSubscription>>(_subscriptions.Values.ToList());
    }
}
