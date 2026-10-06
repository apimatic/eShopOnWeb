using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Services;

public class MaxioSubscriptionServiceTests
{
    private const string FamilyHandle = "eshop-subscribe";

private static MaxioOptions CreateOptions() => new()
    {
        ApiKey = "test-api-key",
        Subdomain = "cp-exp-3",
        ProductFamilyHandle = FamilyHandle
    };

    private static MaxioSubscriptionService CreateService(FakeHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://cp-exp-3.chargify.com")
        };
        return new MaxioSubscriptionService(httpClient, Options.Create(CreateOptions()), NullLogger<MaxioSubscriptionService>.Instance);
    }

    [Fact]
    public async Task ListPlansAsyncReturnsOnlyPlansInConfiguredFamily()
    {
        var handler = new FakeHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/products.json")
            {
                return FakeHttpMessageHandler.Json(HttpStatusCode.OK, """
                    [
                      {"product":{"id":1,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month","product_family":{"id":10,"name":"eShopSubscribe","handle":"eshop-subscribe"}}},
                      {"product":{"id":2,"name":"Other Plan","handle":"other-plan","price_in_cents":1000,"interval":1,"interval_unit":"month","product_family":{"id":11,"name":"Other","handle":"other-family"}}}
                    ]
                    """);
            }

            if (request.RequestUri!.AbsolutePath == "/components.json")
            {
                return FakeHttpMessageHandler.Json(HttpStatusCode.OK, """
                    [
                      {"component":{"id":5,"name":"API Calls","handle":"api-call","kind":"metered_component","unit_name":"api call","unit_price":"0.01","product_family_handle":"eshop-subscribe"}}
                    ]
                    """);
            }

            return FakeHttpMessageHandler.Json(HttpStatusCode.NotFound, "{}");
        });

        var service = CreateService(handler);
        var plans = await service.ListPlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.Equal(FamilyHandle, plan.ProductFamilyHandle);
        var component = Assert.Single(plan.Components);
        Assert.Equal("api-call", component.Handle);
        Assert.Equal(0.01m, component.UnitPrice);
    }

    [Fact]
    public async Task SubscribeAsyncCreatesCustomerThenSubscriptionWhenCustomerDoesNotExist()
    {
        var handler = new FakeHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path == "/customers/lookup.json")
            {
                return FakeHttpMessageHandler.Json(HttpStatusCode.NotFound, "{}");
            }

            if (path == "/customers.json" && request.Method == HttpMethod.Post)
            {
                return FakeHttpMessageHandler.Json(HttpStatusCode.Created, """
                    {"customer":{"id":100,"first_name":"demo","last_name":"Shopper","email":"demouser@microsoft.com","reference":"demouser@microsoft.com"}}
                    """);
            }

            if (path == "/customers/100/subscriptions.json")
            {
                return FakeHttpMessageHandler.Json(HttpStatusCode.OK, "[]");
            }

            if (path == "/subscriptions.json" && request.Method == HttpMethod.Post)
            {
                return FakeHttpMessageHandler.Json(HttpStatusCode.Created, """
                    {"subscription":{"id":500,"state":"active","product_price_in_cents":29900,"current_period_ends_at":"2026-11-06T13:29:32+05:00","next_assessment_at":"2026-11-06T13:29:32+05:00","activated_at":"2026-10-06T13:29:34+05:00","created_at":"2026-10-06T13:29:32+05:00","payment_collection_method":"remittance","customer":{"id":100,"reference":"demouser@microsoft.com"},"product":{"id":1,"name":"Pro Plan","handle":"eshop-pro"}}}
                    """);
            }

            return FakeHttpMessageHandler.Json(HttpStatusCode.NotFound, "{}");
        });

        var service = CreateService(handler);
        var result = await service.SubscribeAsync("demouser@microsoft.com", "demouser@microsoft.com", "eshop-pro");

        Assert.True(result.Created);
        Assert.Equal(500, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal("eshop-pro", result.Subscription.PlanHandle);
        Assert.Equal(29900, result.Subscription.ProductPriceInCents);
        Assert.Equal("remittance", result.Subscription.PaymentCollectionMethod);

        var post = handler.Requests.Single(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath == "/subscriptions.json");
        var body = await post.Content!.ReadAsStringAsync();
        Assert.Contains("\"product_handle\":\"eshop-pro\"", body);
        Assert.Contains("\"customer_id\":100", body);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", body);
    }

    [Fact]
    public async Task SubscribeAsyncReturnsExistingSubscriptionWhenAlreadySubscribed()
    {
        var handler = new FakeHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path == "/customers/lookup.json")
            {
                return FakeHttpMessageHandler.Json(HttpStatusCode.OK, """
                    {"customer":{"id":100,"first_name":"demo","last_name":"Shopper","email":"demouser@microsoft.com","reference":"demouser@microsoft.com"}}
                    """);
            }

            if (path == "/customers/100/subscriptions.json")
            {
                return FakeHttpMessageHandler.Json(HttpStatusCode.OK, """
                    [
                      {"subscription":{"id":500,"state":"active","product_price_in_cents":29900,"current_period_ends_at":"2026-11-06T13:29:32+05:00","next_assessment_at":"2026-11-06T13:29:32+05:00","activated_at":"2026-10-06T13:29:34+05:00","created_at":"2026-10-06T13:29:32+05:00","payment_collection_method":"remittance","customer":{"id":100,"reference":"demouser@microsoft.com"},"product":{"id":1,"name":"Pro Plan","handle":"eshop-pro"}}}
                    ]
                    """);
            }

            return FakeHttpMessageHandler.Json(HttpStatusCode.NotFound, "{}");
        });

        var service = CreateService(handler);
        var result = await service.SubscribeAsync("demouser@microsoft.com", "demouser@microsoft.com", "eshop-pro");

        Assert.False(result.Created);
        Assert.Equal(500, result.Subscription.Id);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath == "/subscriptions.json");
    }

    [Fact]
    public async Task SubscribeAsyncAllowsSubscriptionToDifferentPlan()
    {
        var handler = new FakeHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path == "/customers/lookup.json")
            {
                return FakeHttpMessageHandler.Json(HttpStatusCode.OK, """
                    {"customer":{"id":100,"first_name":"demo","last_name":"Shopper","email":"demouser@microsoft.com","reference":"demouser@microsoft.com"}}
                    """);
            }

            if (path == "/customers/100/subscriptions.json")
            {
                return FakeHttpMessageHandler.Json(HttpStatusCode.OK, """
                    [
                      {"subscription":{"id":500,"state":"active","product_price_in_cents":29900,"current_period_ends_at":"2026-11-06T13:29:32+05:00","next_assessment_at":"2026-11-06T13:29:32+05:00","activated_at":"2026-10-06T13:29:34+05:00","created_at":"2026-10-06T13:29:32+05:00","payment_collection_method":"remittance","customer":{"id":100,"reference":"demouser@microsoft.com"},"product":{"id":1,"name":"Pro Plan","handle":"eshop-pro"}}}
                    ]
                    """);
            }

            if (path == "/subscriptions.json" && request.Method == HttpMethod.Post)
            {
                return FakeHttpMessageHandler.Json(HttpStatusCode.Created, """
                    {"subscription":{"id":501,"state":"active","product_price_in_cents":2900,"current_period_ends_at":"2026-11-06T13:29:32+05:00","next_assessment_at":"2026-11-06T13:29:32+05:00","activated_at":"2026-10-06T13:29:34+05:00","created_at":"2026-10-06T13:29:32+05:00","payment_collection_method":"remittance","customer":{"id":100,"reference":"demouser@microsoft.com"},"product":{"id":2,"name":"Basic Plan","handle":"basic-plan"}}}
                    """);
            }

            return FakeHttpMessageHandler.Json(HttpStatusCode.NotFound, "{}");
        });

        var service = CreateService(handler);
        var result = await service.SubscribeAsync("demouser@microsoft.com", "demouser@microsoft.com", "basic-plan");

        Assert.True(result.Created);
        Assert.Equal(501, result.Subscription.Id);
        Assert.Equal("basic-plan", result.Subscription.PlanHandle);
    }

    [Fact]
    public async Task ListSubscriptionsAsyncReturnsEmptyWhenNoCustomerExists()
    {
        var handler = new FakeHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/customers/lookup.json")
            {
                return FakeHttpMessageHandler.Json(HttpStatusCode.NotFound, "{}");
            }

            return FakeHttpMessageHandler.Json(HttpStatusCode.NotFound, "{}");
        });

        var service = CreateService(handler);
        var subscriptions = await service.ListSubscriptionsAsync("nobody@example.com");

        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task ThrowsMaxioApiExceptionOnUpstreamError()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            FakeHttpMessageHandler.Json(HttpStatusCode.InternalServerError, "{\"errors\":[\"boom\"]}"));

        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<MaxioApiException>(() => service.ListPlansAsync());
        Assert.Equal(500, ex.StatusCode);
        Assert.Contains("boom", ex.ResponseBody);
    }
}
