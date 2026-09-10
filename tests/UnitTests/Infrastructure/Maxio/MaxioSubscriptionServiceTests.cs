using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionServiceTests
{
    private const string FamilyHandle = "eshop-subscribe";
    private static readonly SubscriberIdentity Subscriber = new()
    {
        Reference = "demouser@microsoft.com",
        Email = "demouser@microsoft.com",
    };

    private readonly FakeMaxioApiClient _api = new();

    private MaxioSubscriptionService CreateService()
    {
        _api.Products.Add(new ProductWire { Id = 1, Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" });
        _api.Products.Add(new ProductWire { Id = 2, Handle = "basic-plan", Name = "Basic Plan", PriceInCents = 2900, Interval = 1, IntervalUnit = "month" });

        var settings = Options.Create(new MaxioSettings { ApiKey = "k", Subdomain = "sub", ProductFamilyHandle = FamilyHandle });
        return new MaxioSubscriptionService(_api, settings, new MemoryCache(new MemoryCacheOptions()), NullLogger<MaxioSubscriptionService>.Instance);
    }

    [Fact]
    public async Task ListPlans_MapsProducts_OrdersByPrice_AndConvertsCents()
    {
        var service = CreateService();

        var plans = await service.ListPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Equal("basic-plan", plans[0].Handle); // cheaper first
        Assert.Equal(29.00m, plans[0].Price);
        Assert.Equal("eshop-pro", plans[1].Handle);
        Assert.Equal(299.00m, plans[1].Price);
        Assert.Equal("month", plans[1].IntervalUnit);
    }

    [Fact]
    public async Task ListPlans_ExcludesArchivedProducts()
    {
        var service = CreateService();
        _api.Products.Add(new ProductWire { Id = 3, Handle = "legacy", Name = "Legacy", PriceInCents = 100, Interval = 1, IntervalUnit = "month", ArchivedAt = System.DateTimeOffset.UtcNow });

        var plans = await service.ListPlansAsync();

        Assert.DoesNotContain(plans, p => p.Handle == "legacy");
    }

    [Fact]
    public async Task Subscribe_CreatesCustomerAndSubscription_WhenNoneExist()
    {
        var service = CreateService();

        var result = await service.SubscribeAsync(Subscriber, "eshop-pro");

        Assert.False(result.AlreadyExisted);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal("eshop-pro", result.Subscription.PlanHandle);
        Assert.Equal(299.00m, result.Subscription.Price);
        Assert.Equal("remittance", result.Subscription.PaymentCollectionMethod);
        Assert.NotNull(result.Subscription.NextBillingDate);
        Assert.Equal(1, _api.CreateCustomerCalls);
        Assert.Equal(1, _api.CreateSubscriptionCalls);
    }

    [Fact]
    public async Task Subscribe_IsIdempotent_ReturnsExistingLiveSubscription()
    {
        var service = CreateService();

        var first = await service.SubscribeAsync(Subscriber, "eshop-pro");
        var second = await service.SubscribeAsync(Subscriber, "eshop-pro");

        Assert.False(first.AlreadyExisted);
        Assert.True(second.AlreadyExisted);
        Assert.Equal(first.Subscription.Id, second.Subscription.Id);
        Assert.Equal(1, _api.CreateCustomerCalls);   // customer reused
        Assert.Equal(1, _api.CreateSubscriptionCalls); // no duplicate subscription
    }

    [Fact]
    public async Task Subscribe_ReusesExistingCustomer_WhenAlreadyPresent()
    {
        var service = CreateService();
        _api.Customers.Add(new CustomerWire { Id = 42, Reference = Subscriber.Reference, Email = Subscriber.Email, FirstName = "Demo", LastName = "User" });

        var result = await service.SubscribeAsync(Subscriber, "basic-plan");

        Assert.Equal(0, _api.CreateCustomerCalls);
        Assert.Equal(1, _api.CreateSubscriptionCalls);
        Assert.False(result.AlreadyExisted);
        Assert.Equal(42, _api.Subscriptions.Single().Customer!.Id); // subscription bound to the existing customer
    }

    [Fact]
    public async Task Subscribe_CanceledSubscription_DoesNotBlockResubscribe()
    {
        var service = CreateService();
        _api.Customers.Add(new CustomerWire { Id = 7, Reference = Subscriber.Reference, Email = Subscriber.Email });
        var pro = _api.Products.First(p => p.Handle == "eshop-pro");
        _api.Subscriptions.Add(new SubscriptionWire { Id = 999, State = "canceled", Product = pro, Customer = _api.Customers[0] });

        var result = await service.SubscribeAsync(Subscriber, "eshop-pro");

        Assert.False(result.AlreadyExisted);
        Assert.Equal(1, _api.CreateSubscriptionCalls);
    }

    [Fact]
    public async Task Subscribe_ThrowsPlanNotFound_ForUnknownHandle()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => service.SubscribeAsync(Subscriber, "does-not-exist"));
        Assert.Equal(0, _api.CreateSubscriptionCalls);
    }

    [Fact]
    public async Task ListSubscriptions_ReturnsEmpty_WhenNoCustomer()
    {
        var service = CreateService();

        var subs = await service.ListSubscriptionsAsync(Subscriber);

        Assert.Empty(subs);
    }
}
