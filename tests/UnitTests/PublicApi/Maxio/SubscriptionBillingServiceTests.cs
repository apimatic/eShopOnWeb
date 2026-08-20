using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.PublicApi.Maxio;

public class SubscriptionBillingServiceTests
{
    [Fact]
    public async Task ConcurrentDuplicateRequestsCreateOnlyOneCustomerAndSubscription()
    {
        await using var context = CreateContext();
        var maxio = new RecordingMaxioClient();
        var service = CreateService(maxio, context);
        var shopper = new ShopperIdentity("user-123", "demo@example.com");

        var results = await Task.WhenAll(
            service.SubscribeAsync(shopper, "eshop-pro", CancellationToken.None),
            service.SubscribeAsync(shopper, "eshop-pro", CancellationToken.None));

        Assert.Equal(1, maxio.CustomerCreateCount);
        Assert.Equal(1, maxio.SubscriptionCreateCount);
        Assert.Single(context.SubscriptionBillingRecords);
        Assert.Single(results, result => result.Created);
        Assert.Single(results, result => !result.Created);
        Assert.All(results, result => Assert.Equal(501, result.Subscription.Id));
    }

    [Fact]
    public async Task PlansAndSubscriptionsAreFilteredToConfiguredFamily()
    {
        await using var context = CreateContext();
        var maxio = new RecordingMaxioClient(includeOtherFamily: true);
        var service = CreateService(maxio, context);
        var shopper = new ShopperIdentity("user-123", "demo@example.com");

        var plans = await service.GetPlansAsync(CancellationToken.None);
        await service.SubscribeAsync(shopper, "eshop-pro", CancellationToken.None);
        var subscriptions = await service.GetSubscriptionsAsync(shopper, CancellationToken.None);

        Assert.Single(plans);
        Assert.Equal("eshop-pro", plans[0].ProductHandle);
        Assert.Single(subscriptions);
        Assert.Equal("eshop-pro", subscriptions[0].ProductHandle);
        Assert.Equal(29900, subscriptions[0].PriceInCents);
        Assert.Equal("active", subscriptions[0].State);
        Assert.NotNull(subscriptions[0].NextBillingAt);
    }

    private static CatalogContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase($"MaxioTests-{Guid.NewGuid()}")
            .Options;
        return new CatalogContext(options);
    }

    private static SubscriptionBillingService CreateService(IMaxioClient maxio, CatalogContext context) =>
        new(maxio, context, new SubscriptionOperationLock(), Options.Create(new MaxioOptions
        {
            ApiKey = "test",
            Subdomain = "test",
            ProductFamilyHandle = "billing-family"
        }));

    private sealed class RecordingMaxioClient : IMaxioClient
    {
        private readonly object _sync = new();
        private readonly MaxioProduct _plan = Product("eshop-pro", "billing-family", 29900);
        private readonly IReadOnlyList<MaxioProduct> _products;
        private MaxioCustomer? _customer;
        private MaxioSubscription? _subscription;

        public RecordingMaxioClient(bool includeOtherFamily = false)
        {
            _products = includeOtherFamily
                ? new[] { _plan, Product("other", "other-family", 100) }
                : new[] { _plan };
        }

        public int CustomerCreateCount { get; private set; }
        public int SubscriptionCreateCount { get; private set; }

        public Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_products);

        public Task<MaxioProduct?> ReadProductByHandleAsync(string productHandle, CancellationToken cancellationToken) =>
            Task.FromResult<MaxioProduct?>(productHandle == _plan.Handle ? _plan : null);

        public Task<MaxioCustomer?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                return Task.FromResult(_customer);
            }
        }

        public Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                CustomerCreateCount++;
                _customer = new MaxioCustomer(101, customer.FirstName, customer.LastName, customer.Email, customer.Reference);
                return Task.FromResult(_customer);
            }
        }

        public Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
        {
            IReadOnlyList<MaxioSubscription> result = _subscription is null
                ? Array.Empty<MaxioSubscription>()
                : new[] { _subscription, Subscription(999, Product("other", "other-family", 100), _customer!, "other-ref") };
            return Task.FromResult(result);
        }

        public Task<MaxioSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                return Task.FromResult(_subscription?.Reference == reference ? _subscription : null);
            }
        }

        public Task<MaxioSubscription?> ReadSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken) =>
            Task.FromResult(_subscription?.Id == subscriptionId ? _subscription : null);

        public Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                SubscriptionCreateCount++;
                _subscription = Subscription(501, _plan, _customer!, subscription.Reference);
                return Task.FromResult(_subscription);
            }
        }

        private static MaxioProduct Product(string handle, string familyHandle, long price) =>
            new(7, handle, handle, "Plan", price, 1, "month", null, false,
                new MaxioProductFamily(3, familyHandle, familyHandle));

        private static MaxioSubscription Subscription(
            int id, MaxioProduct product, MaxioCustomer customer, string reference) =>
            new(id, "active", product.PriceInCents, DateTimeOffset.UtcNow.AddMonths(1),
                DateTimeOffset.UtcNow.AddMonths(1), reference, "USD", customer, product);
    }
}
