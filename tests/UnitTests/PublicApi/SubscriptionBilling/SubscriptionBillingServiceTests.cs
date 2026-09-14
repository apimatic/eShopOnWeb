using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.SubscriptionBilling;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.PublicApi.SubscriptionBilling;

public class SubscriptionBillingServiceTests
{
    [Fact]
    public async Task ListPlansFiltersConfiguredFamilyAndMapsPrices()
    {
        await using var context = NewContext();
        var maxio = new FakeMaxioClient();
        maxio.Products.Add(NewProduct("basic-plan", 2900, "test-family"));
        maxio.Products.Add(NewProduct("other-plan", 100, "another-family"));
        var service = NewService(context, maxio);

        var plans = await service.ListPlansAsync(CancellationToken.None);

        var plan = Assert.Single(plans);
        Assert.Equal("basic-plan", plan.Handle);
        Assert.Equal(29m, plan.Price);
        Assert.Equal("USD", plan.Currency);
    }

    [Fact]
    public async Task RepeatedSubscribeCreatesOnlyOneCustomerAndSubscription()
    {
        await using var context = NewContext();
        var maxio = new FakeMaxioClient();
        maxio.Products.Add(NewProduct("eshop-pro", 29900, "test-family"));
        var service = NewService(context, maxio);
        var shopper = new Shopper("user-123", "demo.user@example.com");

        var first = await service.SubscribeAsync(shopper, "eshop-pro", CancellationToken.None);
        var second = await service.SubscribeAsync(shopper, "eshop-pro", CancellationToken.None);

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.Subscription.Id, second.Subscription.Id);
        Assert.Equal(1, maxio.CreateCustomerCalls);
        Assert.Equal(1, maxio.CreateSubscriptionCalls);
        Assert.Single(context.BillingCustomers);
        var enrollment = Assert.Single(context.SubscriptionEnrollments);
        Assert.Equal(first.Subscription.Id, enrollment.MaxioSubscriptionId);
    }

    [Fact]
    public async Task ListSubscriptionsReturnsLiveMaxioState()
    {
        await using var context = NewContext();
        var maxio = new FakeMaxioClient();
        maxio.Products.Add(NewProduct("basic-plan", 2900, "test-family"));
        var shopper = new Shopper("user-456", "shopper@example.com");
        var service = NewService(context, maxio);
        await service.SubscribeAsync(shopper, "basic-plan", CancellationToken.None);
        maxio.Subscriptions.Single().State = "past_due";

        var subscriptions = await service.ListSubscriptionsAsync(shopper, CancellationToken.None);

        var subscription = Assert.Single(subscriptions);
        Assert.Equal("past_due", subscription.State);
        Assert.Equal(29m, subscription.Price);
        Assert.NotNull(subscription.NextBillingAt);
    }

    private static CatalogContext NewContext()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new CatalogContext(options);
    }

    private static SubscriptionBillingService NewService(CatalogContext context, IMaxioClient maxio)
    {
        return new SubscriptionBillingService(
            context,
            maxio,
            new SubscriptionOperationLock(),
            Options.Create(new MaxioOptions
            {
                ApiKey = "test-key",
                Subdomain = "test-site",
                ProductFamilyHandle = "test-family"
            }));
    }

    private static MaxioProduct NewProduct(string handle, long priceInCents, string familyHandle)
    {
        return new MaxioProduct
        {
            Id = priceInCents,
            Handle = handle,
            Name = handle,
            PriceInCents = priceInCents,
            Interval = 1,
            IntervalUnit = "month",
            ProductFamily = new MaxioProductFamily { Id = 1, Handle = familyHandle }
        };
    }

    private sealed class FakeMaxioClient : IMaxioClient
    {
        private long _nextCustomerId = 100;
        private long _nextSubscriptionId = 200;

        public List<MaxioProduct> Products { get; } = new();
        public List<MaxioCustomer> Customers { get; } = new();
        public List<MaxioSubscription> Subscriptions { get; } = new();
        public int CreateCustomerCalls { get; private set; }
        public int CreateSubscriptionCalls { get; private set; }

        public Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new MaxioSite { Currency = "USD", Test = true });

        public Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MaxioProduct>>(Products);

        public Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken) =>
            Task.FromResult(Customers.SingleOrDefault(customer => customer.Reference == reference));

        public Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken)
        {
            CreateCustomerCalls++;
            var created = new MaxioCustomer
            {
                Id = _nextCustomerId++,
                Email = customer.Email,
                Reference = customer.Reference
            };
            Customers.Add(created);
            return Task.FromResult(created);
        }

        public Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MaxioSubscription>>(Subscriptions.Where(subscription => subscription.Customer.Id == customerId).ToList());

        public Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken) =>
            Task.FromResult(Subscriptions.SingleOrDefault(subscription => subscription.Reference == reference));

        public Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken)
        {
            CreateSubscriptionCalls++;
            var customer = Customers.Single(item => item.Id == subscription.CustomerId);
            var product = Products.Single(item => item.Handle == subscription.ProductHandle);
            var created = new MaxioSubscription
            {
                Id = _nextSubscriptionId++,
                State = "active",
                ProductPriceInCents = product.PriceInCents,
                CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddMonths(1),
                NextAssessmentAt = DateTimeOffset.UtcNow.AddMonths(1),
                Reference = subscription.Reference,
                Currency = "USD",
                Customer = customer,
                Product = product
            };
            Subscriptions.Add(created);
            return Task.FromResult(created);
        }
    }
}
