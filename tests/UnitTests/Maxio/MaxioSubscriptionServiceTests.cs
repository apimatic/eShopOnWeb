using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

public class MaxioSubscriptionServiceTests
{
    private class FakeClient : IMaxioApiClient
    {
        public List<MaxioProduct> Products { get; } = new()
        {
            new MaxioProduct { Id = 1, Handle = "pro", Name = "Pro", PriceInCents = 29900, Interval = 1, IntervalUnit = "month", ProductFamily = new MaxioProductFamilyRef { Handle = "fam" } },
            new MaxioProduct { Id = 2, Handle = "old", Name = "Old", PriceInCents = 100, ArchivedAt = DateTimeOffset.UtcNow }
        };

        public Dictionary<string, MaxioCustomer> Customers { get; } = new();
        public List<MaxioSubscription> Subscriptions { get; } = new();
        public int CustomerCreates { get; private set; }
        public int SubscriptionCreates { get; private set; }
        public bool FailFirstCustomerCreateAsDuplicate { get; set; }

        public Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(string productFamilyHandle, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<MaxioProduct>>(Products);

        public Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
            => Task.FromResult(Customers.TryGetValue(reference, out var c) ? c : null);

        public Task<MaxioCustomer> CreateCustomerAsync(MaxioNewCustomer customer, CancellationToken cancellationToken)
        {
            CustomerCreates++;
            var created = new MaxioCustomer { Id = 100 + CustomerCreates, Email = customer.Email, Reference = customer.Reference };
            Customers[customer.Reference] = created;
            if (FailFirstCustomerCreateAsDuplicate)
            {
                FailFirstCustomerCreateAsDuplicate = false;
                throw new BillingProviderException(BillingFailureKind.Rejected, "Reference: must be unique");
            }

            return Task.FromResult(created);
        }

        public Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<MaxioSubscription>>(Subscriptions.Where(s => s.Customer?.Id == customerId).ToList());

        public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioNewSubscription subscription, CancellationToken cancellationToken)
        {
            await Task.Delay(20, cancellationToken);
            SubscriptionCreates++;
            var product = Products.First(p => p.Handle == subscription.ProductHandle);
            var created = new MaxioSubscription
            {
                Id = 500 + SubscriptionCreates,
                State = "active",
                ProductPriceInCents = product.PriceInCents,
                NextAssessmentAt = DateTimeOffset.UtcNow.AddMonths(1),
                Product = product,
                Customer = Customers.Values.First(c => c.Id == subscription.CustomerId),
                PaymentCollectionMethod = subscription.PaymentCollectionMethod
            };
            Subscriptions.Add(created);
            return created;
        }
    }

    private static MaxioSubscriptionService Create(FakeClient client) => new(
        client,
        new MemoryCache(new MemoryCacheOptions()),
        Options.Create(new MaxioOptions { ApiKey = "k", Subdomain = "s", ProductFamilyHandle = "fam", PlanCacheSeconds = 0 }),
        NullLogger<MaxioSubscriptionService>.Instance);

    private static readonly SubscriberIdentity Alice = new("user-1", "alice@example.com");

    [Fact]
    public async Task PlansExcludeArchivedProducts()
    {
        var plans = await Create(new FakeClient()).GetPlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal("pro", plan.Handle);
        Assert.Equal(299m, plan.Price);
    }

    [Fact]
    public async Task SubscribeCreatesCustomerAndSubscriptionOnce()
    {
        var client = new FakeClient();
        var result = await Create(client).SubscribeAsync(Alice, "pro");

        Assert.True(result.Created);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal(299m, result.Subscription.Price);
        Assert.NotNull(result.Subscription.NextBillingDate);
        Assert.Equal("remittance", result.Subscription.PaymentCollectionMethod);
        Assert.Equal(1, client.CustomerCreates);
        Assert.Equal("eshop-user:user-1", client.Customers.Keys.Single());
    }

    [Fact]
    public async Task RepeatedSubscribeIsIdempotent()
    {
        var client = new FakeClient();
        var service = Create(client);

        var first = await service.SubscribeAsync(Alice, "pro");
        var second = await service.SubscribeAsync(Alice, "pro");

        Assert.False(second.Created);
        Assert.Equal(first.Subscription.SubscriptionId, second.Subscription.SubscriptionId);
        Assert.Equal(1, client.CustomerCreates);
        Assert.Equal(1, client.SubscriptionCreates);
    }

    [Fact]
    public async Task ConcurrentSubscribeCreatesSingleSubscription()
    {
        var client = new FakeClient();
        var service = Create(client);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => service.SubscribeAsync(Alice, "pro")));

        Assert.Equal(1, results.Count(r => r.Created));
        Assert.Equal(1, client.SubscriptionCreates);
        Assert.Equal(1, client.CustomerCreates);
    }

    [Fact]
    public async Task CanceledSubscriptionAllowsResubscribe()
    {
        var client = new FakeClient();
        var service = Create(client);
        await service.SubscribeAsync(Alice, "pro");
        client.Subscriptions[0].State = "canceled";

        var again = await service.SubscribeAsync(Alice, "pro");

        Assert.True(again.Created);
        Assert.Equal(2, client.SubscriptionCreates);
    }

    [Fact]
    public async Task DuplicateCustomerReferenceRaceRecoversByLookup()
    {
        var client = new FakeClient { FailFirstCustomerCreateAsDuplicate = true };

        var result = await Create(client).SubscribeAsync(Alice, "pro");

        Assert.True(result.Created);
        Assert.Equal(1, client.SubscriptionCreates);
    }

    [Fact]
    public async Task UnknownOrArchivedPlanIsRejectedBeforeAnyCustomerIsCreated()
    {
        var client = new FakeClient();
        var service = Create(client);

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(() => service.SubscribeAsync(Alice, "nope"));
        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(() => service.SubscribeAsync(Alice, "old"));
        Assert.Equal(0, client.CustomerCreates);
    }

    [Fact]
    public async Task MySubscriptionsAreScopedToCallerAndEmptyWithoutCustomer()
    {
        var client = new FakeClient();
        var service = Create(client);

        Assert.Empty(await service.GetSubscriptionsAsync(Alice));

        await service.SubscribeAsync(Alice, "pro");
        await service.SubscribeAsync(new SubscriberIdentity("user-2", "bob@example.com"), "pro");

        var mine = await service.GetSubscriptionsAsync(Alice);
        var subscription = Assert.Single(mine);
        Assert.Equal("pro", subscription.PlanHandle);
        Assert.Equal(client.Customers["eshop-user:user-1"].Id, subscription.CustomerId);
    }
}
