using System.Net;
using System.Net.Http;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Options;
using Xunit;
using static Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio.RecordingHandler;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionServiceTests
{
    private const string ProductFamilyHandle = "eshop-subscribe";
    private const string CustomerReference = "demouser@microsoft.com";
    private const string PlanHandle = "eshop-pro";

    private static (MaxioSubscriptionService Service, RecordingHandler Handler) CreateService(
        params Func<HttpRequestMessage, HttpResponseMessage>[] responders)
    {
        var handler = new RecordingHandler(responders);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://cp-exp-4.chargify.com/") };
        var options = Options.Create(new MaxioOptions { ProductFamilyHandle = ProductFamilyHandle, Subdomain = "cp-exp-4" });
        return (new MaxioSubscriptionService(new MaxioApiClient(httpClient), options), handler);
    }

    [Fact]
    public async Task GetSubscriptionPlansAsync_ReturnsOnlyActivePlans_MappedFromMaxioProducts()
    {
        var productsJson = """
        [
          {"product":{"id":1,"name":"Pro Plan","handle":"eshop-pro","description":"The pro plan","price_in_cents":29900,"interval":1,"interval_unit":"month","archived_at":null}},
          {"product":{"id":2,"name":"Retired Plan","handle":"retired-plan","price_in_cents":100,"interval":1,"interval_unit":"month","archived_at":"2020-01-01T00:00:00Z"}}
        ]
        """;
        var (service, handler) = CreateService(RespondWith(HttpStatusCode.OK, productsJson));

        var plans = await service.GetSubscriptionPlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal("Pro Plan", plan.Name);
        Assert.Equal("The pro plan", plan.Description);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.Equal("month", plan.IntervalUnit);

        var request = Assert.Single(handler.Requests);
        Assert.Equal($"/product_families/handle:{ProductFamilyHandle}/products.json", request.PathAndQuery);
    }

    [Fact]
    public async Task SubscribeAsync_FirstTimeSubscriber_CreatesCustomerThenSubscription()
    {
        var (service, handler) = CreateService(
            RespondWith(HttpStatusCode.NotFound), // subscriptions/lookup.json -> no existing subscription
            RespondWith(HttpStatusCode.NotFound), // customers/lookup.json -> no existing customer
            RespondWith(HttpStatusCode.OK, """{"customer":{"id":555,"reference":"demouser@microsoft.com","email":"demouser@microsoft.com"}}"""),
            RespondWith(HttpStatusCode.Created, """
                {"subscription":{"id":777,"state":"active","product_price_in_cents":29900,
                "current_period_ends_at":"2026-10-05T00:00:00Z","next_assessment_at":"2026-10-05T00:00:00Z",
                "activated_at":"2026-09-05T00:00:00Z","created_at":"2026-09-05T00:00:00Z",
                "product":{"id":1,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900}}}
                """));

        var result = await service.SubscribeAsync(CustomerReference, CustomerReference, PlanHandle);

        Assert.Equal(777, result.SubscriptionId);
        Assert.Equal("active", result.State);
        Assert.Equal("eshop-pro", result.PlanHandle);
        Assert.Equal("Pro Plan", result.PlanName);
        Assert.Equal(29900, result.PriceInCents);
        Assert.NotNull(result.NextAssessmentAt);

        Assert.Equal(4, handler.Requests.Count);
        Assert.Contains("subscriptions/lookup.json?reference=eshop%3Ademouser%40microsoft.com%3Aeshop-pro", handler.Requests[0].PathAndQuery);
        Assert.Contains("customers/lookup.json?reference=demouser%40microsoft.com", handler.Requests[1].PathAndQuery);

        Assert.Equal(HttpMethod.Post, handler.Requests[2].Method);
        Assert.Equal("/customers.json", handler.Requests[2].PathAndQuery);
        Assert.Contains("\"reference\":\"demouser@microsoft.com\"", handler.Requests[2].Body);

        Assert.Equal(HttpMethod.Post, handler.Requests[3].Method);
        Assert.Equal("/subscriptions.json", handler.Requests[3].PathAndQuery);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", handler.Requests[3].Body);
        Assert.Contains("\"customer_id\":555", handler.Requests[3].Body);
        Assert.Contains("\"reference\":\"eshop:demouser@microsoft.com:eshop-pro\"", handler.Requests[3].Body);
    }

    [Fact]
    public async Task SubscribeAsync_RepeatedCallForSamePlan_IsIdempotentAndCreatesNothing()
    {
        var (service, handler) = CreateService(
            RespondWith(HttpStatusCode.OK, """
                {"subscription":{"id":777,"state":"active","product_price_in_cents":29900,
                "current_period_ends_at":"2026-10-05T00:00:00Z","next_assessment_at":"2026-10-05T00:00:00Z",
                "activated_at":"2026-09-05T00:00:00Z","created_at":"2026-09-05T00:00:00Z",
                "product":{"id":1,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900}}}
                """));

        var result = await service.SubscribeAsync(CustomerReference, CustomerReference, PlanHandle);

        Assert.Equal(777, result.SubscriptionId);
        // A double-click short-circuits on the subscription lookup: no customer lookup/create, no subscription create.
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Contains("subscriptions/lookup.json", request.PathAndQuery);
    }

    [Fact]
    public async Task SubscribeAsync_ExistingCustomerNewPlan_SkipsCustomerCreation()
    {
        var (service, handler) = CreateService(
            RespondWith(HttpStatusCode.NotFound), // subscriptions/lookup.json -> no existing subscription for this plan
            RespondWith(HttpStatusCode.OK, """{"customer":{"id":555,"reference":"demouser@microsoft.com","email":"demouser@microsoft.com"}}"""),
            RespondWith(HttpStatusCode.Created, """
                {"subscription":{"id":888,"state":"active","product_price_in_cents":2900,
                "current_period_ends_at":null,"next_assessment_at":null,"activated_at":null,"created_at":"2026-09-05T00:00:00Z",
                "product":{"id":2,"name":"Basic Plan","handle":"basic-plan","price_in_cents":2900}}}
                """));

        var result = await service.SubscribeAsync(CustomerReference, CustomerReference, "basic-plan");

        Assert.Equal(888, result.SubscriptionId);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(HttpMethod.Get, handler.Requests[1].Method);
        Assert.Contains("customers/lookup.json", handler.Requests[1].PathAndQuery);
        Assert.Equal(HttpMethod.Post, handler.Requests[2].Method);
        Assert.Equal("/subscriptions.json", handler.Requests[2].PathAndQuery);
    }

    [Fact]
    public async Task GetSubscriptionsForCustomerAsync_UnknownCustomer_ReturnsEmptyWithoutListingSubscriptions()
    {
        var (service, handler) = CreateService(RespondWith(HttpStatusCode.NotFound));

        var subscriptions = await service.GetSubscriptionsForCustomerAsync(CustomerReference);

        Assert.Empty(subscriptions);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetSubscriptionsForCustomerAsync_ExistingCustomer_ReturnsMappedSubscriptions()
    {
        var (service, handler) = CreateService(
            RespondWith(HttpStatusCode.OK, """{"customer":{"id":555,"reference":"demouser@microsoft.com","email":"demouser@microsoft.com"}}"""),
            RespondWith(HttpStatusCode.OK, """
                [{"subscription":{"id":777,"state":"active","product_price_in_cents":29900,
                "current_period_ends_at":"2026-10-05T00:00:00Z","next_assessment_at":"2026-10-05T00:00:00Z",
                "activated_at":"2026-09-05T00:00:00Z","created_at":"2026-09-05T00:00:00Z",
                "product":{"id":1,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900}}}]
                """));

        var subscriptions = await service.GetSubscriptionsForCustomerAsync(CustomerReference);

        var subscription = Assert.Single(subscriptions);
        Assert.Equal(777, subscription.SubscriptionId);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal("/customers/555/subscriptions.json", handler.Requests[1].PathAndQuery);
    }
}
