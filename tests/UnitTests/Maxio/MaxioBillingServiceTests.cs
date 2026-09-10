using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

public class MaxioBillingServiceTests
{
    // A neutral fixture handle — the real family handle is configuration, never hard-coded.
    private const string FamilyHandle = "test-product-family";
    private static readonly SubscriberIdentity Shopper =
        SubscriberIdentity.FromUsername("shopper@example.com");

    private static MaxioBillingService CreateService(FakeMaxioApiClient client)
    {
        var settings = Options.Create(new MaxioSettings
        {
            ApiKey = "key",
            Subdomain = "sub",
            ProductFamilyHandle = FamilyHandle,
        });
        return new MaxioBillingService(client, settings, NullLogger<MaxioBillingService>.Instance);
    }

    private static FakeMaxioApiClient ClientWithPlans() => new FakeMaxioApiClient()
        .AddProduct("eshop-pro", "Pro Plan", 29900)
        .AddProduct("basic-plan", "Basic Plan", 2900);

    [Fact]
    public async Task GetPlans_ExcludesArchivedProducts()
    {
        var client = new FakeMaxioApiClient()
            .AddProduct("eshop-pro", "Pro Plan", 29900)
            .AddProduct("legacy", "Legacy Plan", 100, archivedAt: System.DateTimeOffset.UtcNow);

        var plans = await CreateService(client).GetPlansAsync();

        Assert.Single(plans);
        Assert.Equal("eshop-pro", plans[0].Handle);
    }

    [Fact]
    public async Task Subscribe_CreatesCustomerAndSubscription_WhenNoneExist()
    {
        var client = ClientWithPlans();
        var service = CreateService(client);

        var result = await service.SubscribeAsync(Shopper, "eshop-pro");

        Assert.False(result.AlreadyExisted);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal("eshop-pro", result.Subscription.PlanHandle);
        Assert.Equal(1, client.CreateCustomerCalls);
        Assert.Equal(1, client.CreateSubscriptionCalls);
    }

    [Fact]
    public async Task Subscribe_IsIdempotent_WhenLiveSubscriptionExists()
    {
        var client = ClientWithPlans();
        var customer = client.SeedCustomer(Shopper.Reference);
        var existing = client.SeedSubscription(customer.Id, "eshop-pro", "active");
        var service = CreateService(client);

        var result = await service.SubscribeAsync(Shopper, "eshop-pro");

        Assert.True(result.AlreadyExisted);
        Assert.Equal(existing.Id, result.Subscription.Id);
        Assert.Equal(0, client.CreateSubscriptionCalls); // no duplicate created
        Assert.Equal(0, client.CreateCustomerCalls); // existing customer reused
    }

    [Fact]
    public async Task Subscribe_AllowsResubscribe_WhenExistingSubscriptionIsCanceled()
    {
        var client = ClientWithPlans();
        var customer = client.SeedCustomer(Shopper.Reference);
        client.SeedSubscription(customer.Id, "eshop-pro", "canceled");
        var service = CreateService(client);

        var result = await service.SubscribeAsync(Shopper, "eshop-pro");

        Assert.False(result.AlreadyExisted);
        Assert.Equal(1, client.CreateSubscriptionCalls);
    }

    [Fact]
    public async Task Subscribe_ReusesExistingCustomer_OnConcurrentCreateRace()
    {
        var client = ClientWithPlans();
        var service = CreateService(client);

        // Simulate a racing create: the first CreateCustomer throws 422 (reference taken), and by then the
        // customer exists (as if created by the concurrent request).
        client.CreateCustomerThrows = () =>
        {
            client.SeedCustomer(Shopper.Reference);
            return new MaxioBillingException("reference has been taken", upstreamStatusCode: 422);
        };

        var result = await service.SubscribeAsync(Shopper, "eshop-pro");

        Assert.False(result.AlreadyExisted);
        Assert.Equal("eshop-pro", result.Subscription.PlanHandle);
        Assert.Equal(1, client.CreateSubscriptionCalls);
    }

    [Fact]
    public async Task Subscribe_Throws404_ForUnknownPlan()
    {
        var client = ClientWithPlans();
        var service = CreateService(client);

        var ex = await Assert.ThrowsAsync<MaxioBillingException>(
            () => service.SubscribeAsync(Shopper, "no-such-plan"));

        Assert.Equal(404, ex.UpstreamStatusCode);
        Assert.Equal(0, client.CreateSubscriptionCalls);
    }

    [Fact]
    public async Task GetSubscriptions_ReturnsEmpty_WhenNoCustomer()
    {
        var client = ClientWithPlans();
        var service = CreateService(client);

        var subs = await service.GetSubscriptionsAsync(Shopper);

        Assert.Empty(subs);
    }

    [Fact]
    public async Task GetSubscriptions_MapsCustomerSubscriptions()
    {
        var client = ClientWithPlans();
        var customer = client.SeedCustomer(Shopper.Reference);
        client.SeedSubscription(customer.Id, "eshop-pro", "active");
        client.SeedSubscription(customer.Id, "basic-plan", "active");
        var service = CreateService(client);

        var subs = await service.GetSubscriptionsAsync(Shopper);

        Assert.Equal(2, subs.Count);
        Assert.Contains(subs, s => s.PlanHandle == "eshop-pro");
        Assert.Contains(subs, s => s.PlanHandle == "basic-plan");
    }
}
