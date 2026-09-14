using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class MaxioSubscriptionBillingServiceTests
{
    private static readonly BillingSubscriber Shopper =
        new("demouser@microsoft.com", "demouser@microsoft.com", "Demo", "User");

    private readonly FakeMaxioApiClient _maxio = new();
    private readonly MaxioOptions _options = new()
    {
        ApiKey = "test-key",
        Subdomain = "acme",
        ProductFamilyHandle = "demo-family",
        // Disable caching so each test sees the fake's current catalogue.
        CatalogCacheSeconds = 0,
    };

    private MaxioSubscriptionBillingService CreateService() => new(
        _maxio,
        Options.Create(_options),
        new MemoryCache(new MemoryCacheOptions()),
        new KeyedAsyncLock(),
        NullLogger<MaxioSubscriptionBillingService>.Instance);

    [Fact]
    public async Task ListsPlansFromTheConfiguredFamilyCheapestFirst()
    {
        var plans = (await CreateService().GetPlansAsync()).ToList();

        Assert.Equal(new[] { "starter-plan", "pro-plan" }, plans.Select(p => p.Handle));
        Assert.Equal(2900, plans[0].PriceInCents);
        Assert.Equal("USD", plans[0].Currency);
    }

    [Fact]
    public async Task OmitsArchivedPlans()
    {
        _maxio.Products.First(p => p.Handle == "starter-plan").ArchivedAt = DateTimeOffset.UtcNow;

        var plans = await CreateService().GetPlansAsync();

        Assert.Equal(new[] { "pro-plan" }, plans.Select(p => p.Handle));
    }

    [Fact]
    public async Task ReportsAMisconfiguredProductFamilyAsAConfigurationProblem()
    {
        _options.ProductFamilyHandle = "not-on-this-site";

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => CreateService().GetPlansAsync());

        Assert.Contains("ProductFamilyHandle", exception.Message);
    }

    [Fact]
    public async Task SubscribingCreatesTheCustomerAndTheSubscription()
    {
        var result = await CreateService().SubscribeAsync(new SubscribeToPlanRequest(Shopper, "pro-plan"));

        Assert.True(result.Created);
        Assert.Equal("active", result.Subscription.State);
        Assert.True(result.Subscription.IsLive);
        Assert.Equal("pro-plan", result.Subscription.PlanHandle);
        Assert.Equal(29900, result.Subscription.PriceInCents);
        Assert.Equal(299m, result.Subscription.Price);
        Assert.NotNull(result.Subscription.NextBillingAt);
        Assert.Equal("remittance", result.Subscription.PaymentCollectionMethod);

        Assert.Single(_maxio.Customers);
        Assert.Equal("eshoponweb:demouser@microsoft.com", _maxio.Customers[0].Reference);
        Assert.Single(_maxio.Subscriptions);
        Assert.Equal("eshoponweb:demouser@microsoft.com:pro-plan", _maxio.Subscriptions[0].Reference);
    }

    [Fact]
    public async Task SubscribingTwiceReturnsTheSameSubscriptionAndCreatesNothing()
    {
        var service = CreateService();

        var first = await service.SubscribeAsync(new SubscribeToPlanRequest(Shopper, "pro-plan"));
        var second = await service.SubscribeAsync(new SubscribeToPlanRequest(Shopper, "pro-plan"));

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.Subscription.Id, second.Subscription.Id);
        Assert.Single(_maxio.Customers);
        Assert.Single(_maxio.Subscriptions);
    }

    [Fact]
    public async Task ConcurrentSubscribesProduceExactlyOneSubscription()
    {
        var service = CreateService();

        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ =>
            service.SubscribeAsync(new SubscribeToPlanRequest(Shopper, "pro-plan"))));

        Assert.Single(results.Where(r => r.Created));
        Assert.Single(results.Select(r => r.Subscription.Id).Distinct());
        Assert.Single(_maxio.Customers);
        Assert.Single(_maxio.Subscriptions);
    }

    [Fact]
    public async Task ResolvesAReferenceCollisionByReturningTheSubscriptionTheOtherWriterCreated()
    {
        // Stand in for a second host racing this one: it lands the same reference in the window
        // between our "already subscribed?" check and our create, which this process's lock cannot
        // cover. Maxio rejects the duplicate and the winner is read back.
        var service = CreateService();
        MaxioSubscriptionBillingServiceTestsHelper.RunOnce(_maxio, () =>
        {
            var customer = _maxio.Customers.Single();
            _maxio.Seed(customer, "pro-plan", "eshoponweb:demouser@microsoft.com:pro-plan", "active");
        });

        var result = await service.SubscribeAsync(new SubscribeToPlanRequest(Shopper, "pro-plan"));

        Assert.False(result.Created);
        Assert.Single(_maxio.Subscriptions);
        Assert.Equal("eshoponweb:demouser@microsoft.com:pro-plan", result.Subscription.Reference);
    }

    [Fact]
    public async Task SubscribingToASecondPlanCreatesASecondSubscription()
    {
        var service = CreateService();

        await service.SubscribeAsync(new SubscribeToPlanRequest(Shopper, "pro-plan"));
        var second = await service.SubscribeAsync(new SubscribeToPlanRequest(Shopper, "starter-plan"));

        Assert.True(second.Created);
        Assert.Single(_maxio.Customers);
        Assert.Equal(2, _maxio.Subscriptions.Count);
    }

    [Fact]
    public async Task ResubscribingAfterCancellationCreatesANewSubscriptionUnderTheNextGeneration()
    {
        var customer = _maxio.SeedCustomer("eshoponweb:demouser@microsoft.com", Shopper.Email);
        _maxio.Seed(customer, "pro-plan", "eshoponweb:demouser@microsoft.com:pro-plan", "canceled");

        var result = await CreateService().SubscribeAsync(new SubscribeToPlanRequest(Shopper, "pro-plan"));

        Assert.True(result.Created);
        Assert.Equal("eshoponweb:demouser@microsoft.com:pro-plan:1", result.Subscription.Reference);
        Assert.Equal(2, _maxio.Subscriptions.Count);
    }

    [Theory]
    [InlineData("trialing")]
    [InlineData("past_due")]
    [InlineData("on_hold")]
    public async Task TreatsEveryNonTerminalStateAsStillSubscribed(string state)
    {
        var customer = _maxio.SeedCustomer("eshoponweb:demouser@microsoft.com", Shopper.Email);
        _maxio.Seed(customer, "pro-plan", "eshoponweb:demouser@microsoft.com:pro-plan", state);

        var result = await CreateService().SubscribeAsync(new SubscribeToPlanRequest(Shopper, "pro-plan"));

        Assert.False(result.Created);
        Assert.Single(_maxio.Subscriptions);
        Assert.Equal(0, _maxio.CreateSubscriptionCalls);
    }

    [Fact]
    public async Task RejectsAPlanThatIsNotInTheConfiguredFamily()
    {
        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => CreateService().SubscribeAsync(new SubscribeToPlanRequest(Shopper, "some-other-product")));

        Assert.Empty(_maxio.Customers);
        Assert.Empty(_maxio.Subscriptions);
    }

    [Fact]
    public async Task RejectsAnUnsupportedCollectionMethodBeforeCallingMaxio()
    {
        await Assert.ThrowsAsync<BillingValidationException>(
            () => CreateService().SubscribeAsync(new SubscribeToPlanRequest(Shopper, "pro-plan", "cash")));

        Assert.Empty(_maxio.Customers);
    }

    [Fact]
    public async Task HonoursAPerRequestCollectionMethodOverride()
    {
        var result = await CreateService().SubscribeAsync(
            new SubscribeToPlanRequest(Shopper, "pro-plan", "automatic"));

        Assert.Equal("automatic", result.Subscription.PaymentCollectionMethod);
    }

    [Fact]
    public async Task ListingSubscriptionsForAShopperWithNoBillingRecordReturnsEmptyAndCreatesNothing()
    {
        var subscriptions = await CreateService().GetSubscriptionsAsync(Shopper);

        Assert.Empty(subscriptions);
        Assert.Empty(_maxio.Customers);
        Assert.Equal(0, _maxio.CreateCustomerCalls);
    }

    [Fact]
    public async Task ListsAShoppersSubscriptionsNewestFirst()
    {
        var service = CreateService();
        await service.SubscribeAsync(new SubscribeToPlanRequest(Shopper, "pro-plan"));
        await Task.Delay(10);
        await service.SubscribeAsync(new SubscribeToPlanRequest(Shopper, "starter-plan"));

        var subscriptions = (await service.GetSubscriptionsAsync(Shopper)).ToList();

        Assert.Equal(2, subscriptions.Count);
        Assert.Equal("starter-plan", subscriptions[0].PlanHandle);
        Assert.Equal("pro-plan", subscriptions[1].PlanHandle);
    }

    [Fact]
    public async Task OneShoppersSubscriptionsAreNotVisibleToAnother()
    {
        var service = CreateService();
        await service.SubscribeAsync(new SubscribeToPlanRequest(Shopper, "pro-plan"));

        var other = new BillingSubscriber("someone@else.com", "someone@else.com", "Someone", "Else");

        Assert.Empty(await service.GetSubscriptionsAsync(other));
    }

    [Fact]
    public async Task RejectedCredentialsSurfaceAsAConfigurationProblemNotAServerError()
    {
        _maxio.FailEveryCallWith = HttpStatusCode.Unauthorized;

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => CreateService().GetPlansAsync());

        Assert.Contains("Maxio:ApiKey", exception.Message);
    }

    [Fact]
    public async Task AnUpstreamServerErrorSurfacesAsAProviderFailure()
    {
        _maxio.FailEveryCallWith = HttpStatusCode.InternalServerError;

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => CreateService().GetPlansAsync());

        Assert.Equal(500, exception.StatusCode);
    }

    [Fact]
    public async Task MissingMaxioSettingsAreReportedBeforeAnyCallIsAttempted()
    {
        _options.ApiKey = null;

        await Assert.ThrowsAsync<BillingConfigurationException>(() => CreateService().GetPlansAsync());
    }
}

/// <summary>Helpers for arranging a race in <see cref="MaxioSubscriptionBillingServiceTests"/>.</summary>
internal static class MaxioSubscriptionBillingServiceTestsHelper
{
    /// <summary>Runs <paramref name="interference"/> exactly once, just before the next create.</summary>
    public static void RunOnce(FakeMaxioApiClient client, Action interference)
    {
        var fired = false;
        client.BeforeCreateSubscription = () =>
        {
            if (!fired)
            {
                fired = true;
                interference();
            }

            return Task.CompletedTask;
        };
    }
}
