using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Services.Maxio;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Services.Maxio;

public class MaxioBillingServiceTests
{
    private static readonly MaxioOptions Options = new()
    {
        ApiKey = "test-api-key",
        Subdomain = "test-site",
        ProductFamilyHandle = "eshop-subscribe"
    };

    private static (MaxioBillingService Service, SequenceHttpMessageHandler Handler) CreateSut()
    {
        var handler = new SequenceHttpMessageHandler();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://test-site.chargify.com/") };
        var logger = Substitute.For<ILogger<MaxioBillingService>>();
        return (new MaxioBillingService(httpClient, Options, logger), handler);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    [Fact]
    public async Task ListPlansAsync_ReturnsOnlyNonArchivedPlans_FromConfiguredFamily()
    {
        var (sut, handler) = CreateSut();
        handler.Then(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/product_families/handle:eshop-subscribe/products.json", request.RequestUri!.AbsolutePath);
            return JsonResponse(HttpStatusCode.OK, """
                [
                  { "product": { "id": 1, "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900, "interval": 1, "interval_unit": "month", "archived_at": null, "product_family": { "id": 9, "handle": "eshop-subscribe", "name": "Subscriptions" } } },
                  { "product": { "id": 2, "handle": "old-plan", "name": "Old Plan", "price_in_cents": 500, "interval": 1, "interval_unit": "month", "archived_at": "2020-01-01T00:00:00Z", "product_family": { "id": 9, "handle": "eshop-subscribe", "name": "Subscriptions" } } }
                ]
                """);
        });

        var plans = await sut.ListPlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal("Pro Plan", plan.Name);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.Equal("month", plan.IntervalUnit);
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerAndSubscription_WhenShopperHasNeither()
    {
        var (sut, handler) = CreateSut();
        handler
            .Then(_ => JsonResponse(HttpStatusCode.NotFound, "")) // GET customers/lookup.json -> no customer yet
            .Then(request =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("/customers.json", request.RequestUri!.AbsolutePath);
                return JsonResponse(HttpStatusCode.Created, """{ "customer": { "id": 555, "reference": "shopper@example.com" } }""");
            })
            .Then(request =>
            {
                Assert.Equal("/customers/555/subscriptions.json", request.RequestUri!.AbsolutePath);
                return JsonResponse(HttpStatusCode.OK, "[]"); // no existing subscriptions
            })
            .Then(request =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("/subscriptions.json", request.RequestUri!.AbsolutePath);
                var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                Assert.Contains("\"customer_id\":555", body);
                Assert.Contains("\"product_handle\":\"eshop-pro\"", body);
                Assert.Contains("\"payment_collection_method\":\"remittance\"", body);
                return JsonResponse(HttpStatusCode.Created, """
                    { "subscription": { "id": 777, "state": "active", "product_price_in_cents": 29900,
                      "next_assessment_at": "2026-10-05T00:00:00Z", "activated_at": "2026-09-05T00:00:00Z", "created_at": "2026-09-05T00:00:00Z",
                      "product": { "id": 1, "handle": "eshop-pro", "name": "Pro Plan", "interval": 1, "interval_unit": "month" } } }
                    """);
            });

        var customer = new MaxioCustomerProfile { Reference = "shopper@example.com", Email = "shopper@example.com", FirstName = "shopper", LastName = "Customer" };
        var subscription = await sut.SubscribeAsync(customer, "eshop-pro");

        Assert.Equal(777, subscription.Id);
        Assert.Equal("active", subscription.State);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal(29900, subscription.PriceInCents);
        Assert.True(subscription.IsLive);
    }

    [Fact]
    public async Task SubscribeAsync_ReturnsExistingLiveSubscription_WithoutCreatingADuplicate()
    {
        var (sut, handler) = CreateSut();
        handler
            .Then(_ => JsonResponse(HttpStatusCode.OK, """{ "customer": { "id": 555, "reference": "shopper@example.com" } }"""))
            .Then(_ => JsonResponse(HttpStatusCode.OK, """
                [
                  { "subscription": { "id": 42, "state": "active", "product_price_in_cents": 29900, "created_at": "2026-09-01T00:00:00Z",
                    "product": { "id": 1, "handle": "eshop-pro", "name": "Pro Plan", "interval": 1, "interval_unit": "month" } } }
                ]
                """));
        // Intentionally no third scripted response: a POST to subscriptions.json would throw and fail the test.

        var customer = new MaxioCustomerProfile { Reference = "shopper@example.com", Email = "shopper@example.com", FirstName = "shopper", LastName = "Customer" };
        var subscription = await sut.SubscribeAsync(customer, "eshop-pro");

        Assert.Equal(42, subscription.Id);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task ListSubscriptionsAsync_ReturnsEmpty_WhenShopperHasNoMaxioCustomerYet()
    {
        var (sut, handler) = CreateSut();
        handler.Then(_ => JsonResponse(HttpStatusCode.NotFound, ""));

        var subscriptions = await sut.ListSubscriptionsAsync("never-subscribed@example.com");

        Assert.Empty(subscriptions);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SubscribeAsync_ThrowsMaxioApiExceptionWithParsedMessage_WhenPlanHandleIsUnknown()
    {
        var (sut, handler) = CreateSut();
        handler
            .Then(_ => JsonResponse(HttpStatusCode.OK, """{ "customer": { "id": 555, "reference": "shopper@example.com" } }"""))
            .Then(_ => JsonResponse(HttpStatusCode.OK, "[]"))
            .Then(_ => JsonResponse(HttpStatusCode.UnprocessableEntity, """{ "errors": ["Product with API Handle 'does-not-exist' does not exist for this site."] }"""));

        var customer = new MaxioCustomerProfile { Reference = "shopper@example.com", Email = "shopper@example.com", FirstName = "shopper", LastName = "Customer" };

        var ex = await Assert.ThrowsAsync<MaxioApiException>(() => sut.SubscribeAsync(customer, "does-not-exist"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, ex.StatusCode);
        Assert.Contains("does not exist for this site", ex.Message);
    }
}
