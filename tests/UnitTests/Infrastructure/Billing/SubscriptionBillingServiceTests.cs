using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.eShopWeb.Infrastructure.Data;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class SubscriptionBillingServiceTests
{
    [Fact]
    public async Task ConcurrentSubscribeCreatesOneCustomerAndOneSubscription()
    {
        var dbOptions = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new CatalogContext(dbOptions);
        var maxio = new FakeMaxioClient();
        var service = new SubscriptionBillingService(maxio, context);
        var shopper = new ShopperBillingIdentity(
            "user-123",
            "shopper@example.com",
            "Demo",
            "Shopper");

        var enrollments = await Task.WhenAll(
            service.SubscribeAsync(shopper, "eshop-pro"),
            service.SubscribeAsync(shopper, "eshop-pro"));
        var first = Assert.Single(enrollments, enrollment => enrollment.Created);
        var second = Assert.Single(enrollments, enrollment => !enrollment.Created);

        Assert.Equal(first.Subscription.Id, second.Subscription.Id);
        Assert.Equal(1, maxio.CustomerCreateCount);
        Assert.Equal(1, maxio.SubscriptionCreateCount);
        Assert.Equal(1, await context.SubscriptionRecords.CountAsync());
        var snapshot = await context.SubscriptionRecords.SingleAsync();
        Assert.Equal("active", snapshot.State);
        Assert.Equal(29900, snapshot.PriceInCents);
        Assert.Equal(first.Subscription.NextBillingAt, snapshot.NextBillingAt);
    }

    private sealed class FakeMaxioClient : IMaxioClient
    {
        private readonly MaxioProduct _product = new()
        {
            Id = 100,
            Handle = "eshop-pro",
            Name = "Pro Plan",
            Description = "Pro",
            PriceInCents = 29900,
            Interval = 1,
            IntervalUnit = "month"
        };
        private MaxioCustomer? _customer;
        private MaxioSubscription? _subscription;

        internal int CustomerCreateCount { get; private set; }
        internal int SubscriptionCreateCount { get; private set; }

        public Task<IReadOnlyList<MaxioProduct>> GetProductsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MaxioProduct>>(new[] { _product });

        public Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken) =>
            Task.FromResult(_customer?.Reference == reference ? _customer : null);

        public Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken)
        {
            CustomerCreateCount++;
            _customer = new MaxioCustomer { Id = 200, Reference = customer.Reference, Email = customer.Email };
            return Task.FromResult(_customer);
        }

        public Task<MaxioSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken) =>
            Task.FromResult(_subscription?.Reference == reference ? _subscription : null);

        public Task<MaxioSubscription> CreateSubscriptionAsync(
            MaxioCreateSubscription subscription,
            CancellationToken cancellationToken)
        {
            SubscriptionCreateCount++;
            _subscription = new MaxioSubscription
            {
                Id = 300,
                Reference = subscription.Reference,
                State = "active",
                ProductPriceInCents = 29900,
                Currency = "USD",
                NextAssessmentAt = new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero),
                Customer = _customer!,
                Product = _product
            };
            return Task.FromResult(_subscription);
        }

        public Task<IReadOnlyList<MaxioSubscription>> GetCustomerSubscriptionsAsync(
            int customerId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MaxioSubscription>>(
                _subscription is null ? Array.Empty<MaxioSubscription>() : new[] { _subscription });
    }
}
