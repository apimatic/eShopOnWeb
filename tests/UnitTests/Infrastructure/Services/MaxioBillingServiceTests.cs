using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Services;

public class MaxioBillingServiceTests
{
    private const string CustomerReference = "shopper@example.com";
    private const string PlanHandle = "eshop-pro";

    private static MaxioBillingService CreateSut(FakeHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example-site.chargify.com/") };
        var options = Options.Create(new MaxioOptions { ProductFamilyHandle = "eshop-subscribe" });
        return new MaxioBillingService(httpClient, options);
    }

    [Fact]
    public async Task ListPlansAsync_MapsProductsFromTheConfiguredProductFamily()
    {
        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, "/product_families/handle:eshop-subscribe/products.json", HttpStatusCode.OK,
                """[{"product":{"id":1,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month"}}]""");

        var plans = await CreateSut(handler).ListPlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal("Pro Plan", plan.Name);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.Equal("month", plan.IntervalUnit);
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerAndSubscription_WhenTheShopperHasNeither()
    {
        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.NotFound, "")
            .When(HttpMethod.Post, "/customers.json", HttpStatusCode.Created,
                """{"customer":{"id":42,"reference":"shopper@example.com"}}""")
            .When(HttpMethod.Get, "/customers/42/subscriptions.json", HttpStatusCode.OK, "[]")
            .When(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.Created,
                """{"subscription":{"id":99,"state":"active","next_assessment_at":"2026-10-01T00:00:00Z","product":{"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900}}}""");

        var result = await CreateSut(handler).SubscribeAsync(CustomerReference, CustomerReference, PlanHandle);

        Assert.True(result.IsNewlyCreated);
        Assert.Equal(99, result.SubscriptionId);
        Assert.Equal("active", result.State);
        Assert.Equal(29900, result.PriceInCents);
        Assert.Equal(1, handler.RequestCountFor(HttpMethod.Post, "/customers.json"));
        Assert.Equal(1, handler.RequestCountFor(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task SubscribeAsync_ReturnsTheExistingSubscription_InsteadOfCreatingADuplicate()
    {
        // Simulates a double-click / retry: the shopper already has a live subscription to this plan.
        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.OK,
                """{"customer":{"id":42,"reference":"shopper@example.com"}}""")
            .When(HttpMethod.Get, "/customers/42/subscriptions.json", HttpStatusCode.OK,
                """[{"subscription":{"id":7,"state":"active","product":{"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900}}}]""");

        var result = await CreateSut(handler).SubscribeAsync(CustomerReference, CustomerReference, PlanHandle);

        Assert.False(result.IsNewlyCreated);
        Assert.Equal(7, result.SubscriptionId);
        Assert.Equal(0, handler.RequestCountFor(HttpMethod.Post, "/customers.json"));
        Assert.Equal(0, handler.RequestCountFor(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task SubscribeAsync_AllowsResubscribing_WhenThePriorSubscriptionHasEnded()
    {
        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.OK,
                """{"customer":{"id":42,"reference":"shopper@example.com"}}""")
            .When(HttpMethod.Get, "/customers/42/subscriptions.json", HttpStatusCode.OK,
                """[{"subscription":{"id":7,"state":"canceled","product":{"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900}}}]""")
            .When(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.Created,
                """{"subscription":{"id":8,"state":"active","product":{"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900}}}""");

        var result = await CreateSut(handler).SubscribeAsync(CustomerReference, CustomerReference, PlanHandle);

        Assert.True(result.IsNewlyCreated);
        Assert.Equal(8, result.SubscriptionId);
    }

    [Fact]
    public async Task ListSubscriptionsForCustomerAsync_ReturnsEmpty_WhenTheShopperHasNoMaxioCustomerYet()
    {
        var handler = new FakeHttpMessageHandler()
            .When(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.NotFound, "");

        var subscriptions = await CreateSut(handler).ListSubscriptionsForCustomerAsync(CustomerReference);

        Assert.Empty(subscriptions);
    }
}
