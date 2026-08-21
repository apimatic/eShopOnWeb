using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.PublicApi.SubscriptionEndpoints;

public class SubscriptionBillingServiceTests
{
    [Fact]
    public async Task ConcurrentDoubleSubmitCreatesOneMaxioSubscription()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new CatalogContext(options);
        var maxio = new ConcurrentFakeMaxioClient();
        var service = new SubscriptionBillingService(maxio, context, new SubscriptionOperationLock());
        var user = new ApplicationUser
        {
            Id = "user-id",
            UserName = "demo@example.test",
            NormalizedUserName = "DEMO@EXAMPLE.TEST",
            Email = "demo@example.test",
            NormalizedEmail = "DEMO@EXAMPLE.TEST"
        };

        var results = await Task.WhenAll(
            service.SubscribeAsync(user, "basic-plan", CancellationToken.None),
            service.SubscribeAsync(user, "basic-plan", CancellationToken.None));

        Assert.Equal(1, maxio.CustomerCreateCount);
        Assert.Equal(1, maxio.SubscriptionCreateCount);
        Assert.Single(results.Where(result => result.Created));
        Assert.Single(results.Where(result => !result.Created));
        Assert.Single(await context.SubscriptionRecords.ToListAsync());
    }

    private sealed class ConcurrentFakeMaxioClient : IMaxioClient
    {
        private readonly MaxioProduct _product = new(
            12, "Basic", "basic-plan", "Plan", 2900, 1, "month", null, false,
            new MaxioProductFamily(5, "Plans", "family"));
        private MaxioCustomer? _customer;
        private MaxioSubscription? _subscription;

        public int CustomerCreateCount { get; private set; }
        public int SubscriptionCreateCount { get; private set; }

        public Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MaxioProduct>>(new[] { _product });

        public Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken) =>
            Task.FromResult(_customer);

        public Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken)
        {
            CustomerCreateCount++;
            _customer = new MaxioCustomer(7, customer.FirstName, customer.LastName, customer.Email, customer.Reference);
            return Task.FromResult(_customer);
        }

        public Task<MaxioSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken) =>
            Task.FromResult(_subscription);

        public async Task<MaxioSubscription> CreateSubscriptionAsync(
            MaxioCreateSubscription subscription,
            CancellationToken cancellationToken)
        {
            SubscriptionCreateCount++;
            await Task.Delay(30, cancellationToken);
            _subscription = new MaxioSubscription(
                99,
                "active",
                2900,
                DateTimeOffset.Parse("2026-09-21T00:00:00Z"),
                DateTimeOffset.Parse("2026-09-21T00:00:00Z"),
                _customer!,
                _product,
                subscription.Reference,
                "USD");
            return _subscription;
        }

        public Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MaxioSubscription>>(_subscription is null ? Array.Empty<MaxioSubscription>() : new[] { _subscription });
    }
}
