using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// Exercises the real <see cref="MaxioApiClient"/> + <see cref="MaxioBillingService"/> stack against
/// scripted HTTP responses, so serialization, request paths/bodies, and idempotency are all covered
/// without touching the live Maxio sandbox.
/// </summary>
public class MaxioBillingServiceTests
{
    private const string FamilyHandle = "eshop-subscribe";

    private const string ProductsJson =
        "[{\"product\":{\"id\":1,\"name\":\"Pro Plan\",\"handle\":\"eshop-pro\",\"price_in_cents\":29900," +
        "\"interval\":1,\"interval_unit\":\"month\",\"product_family\":{\"handle\":\"eshop-subscribe\"}}}," +
        "{\"product\":{\"id\":2,\"name\":\"Basic Plan\",\"handle\":\"basic-plan\",\"price_in_cents\":2900," +
        "\"interval\":1,\"interval_unit\":\"month\",\"product_family\":{\"handle\":\"eshop-subscribe\"}}}]";

    private const string CustomerJson =
        "{\"customer\":{\"id\":555,\"reference\":\"demo@x.com\",\"email\":\"demo@x.com\"}}";

    private static string SubscriptionJson(int id, string state, string handle) =>
        "{\"subscription\":{\"id\":" + id + ",\"state\":\"" + state + "\",\"product_price_in_cents\":29900," +
        "\"current_period_ends_at\":\"2026-09-01T00:00:00Z\",\"next_assessment_at\":\"2026-09-01T00:00:00Z\"," +
        "\"created_at\":\"2026-08-01T00:00:00Z\",\"product\":{\"handle\":\"" + handle + "\",\"name\":\"Pro Plan\"}," +
        "\"customer\":{\"id\":555}}}";

    private static (MaxioBillingService service, RecordingHttpMessageHandler handler) BuildService(
        Func<HttpRequestMessage, string?, HttpResponseMessage> responder)
    {
        var handler = new RecordingHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://test.chargify.com/") };
        var client = new MaxioApiClient(httpClient, Substitute.For<IAppLogger<MaxioApiClient>>());
        var settings = new MaxioSettings { ProductFamilyHandle = FamilyHandle, Subdomain = "test", ApiKey = "k" };
        var service = new MaxioBillingService(client, settings, Substitute.For<IAppLogger<MaxioBillingService>>());
        return (service, handler);
    }

    [Fact]
    public async Task GetPlansAsync_MapsProductsFromConfiguredFamily()
    {
        var (service, handler) = BuildService((req, _) =>
            RecordingHttpMessageHandler.Json(HttpStatusCode.OK, ProductsJson));

        var plans = await service.GetPlansAsync();

        Assert.Equal(2, plans.Count);
        var pro = plans.Single(p => p.Handle == "eshop-pro");
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal("$299.00", pro.FormattedPrice);
        Assert.Contains(handler.Requests, r =>
            r.Method == "GET" && r.PathAndQuery.Contains("product_families/handle:eshop-subscribe/products.json"));
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerAndSubscription_WhenNoneExist()
    {
        var (service, handler) = BuildService((req, _) =>
        {
            var path = req.RequestUri!.PathAndQuery;
            if (req.Method == HttpMethod.Get && path.Contains("/product_families/"))
                return RecordingHttpMessageHandler.Json(HttpStatusCode.OK, ProductsJson);
            if (req.Method == HttpMethod.Get && path.Contains("/customers/lookup.json"))
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            if (req.Method == HttpMethod.Post && path.EndsWith("/customers.json"))
                return RecordingHttpMessageHandler.Json(HttpStatusCode.OK, CustomerJson);
            if (req.Method == HttpMethod.Get && path.Contains("/subscriptions.json"))
                return RecordingHttpMessageHandler.Json(HttpStatusCode.OK, "[]");
            if (req.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json"))
                return RecordingHttpMessageHandler.Json(HttpStatusCode.Created, SubscriptionJson(901, "active", "eshop-pro"));
            throw new InvalidOperationException($"Unexpected {req.Method} {path}");
        });

        var result = await service.SubscribeAsync(new SubscribeRequest
        {
            UserReference = "demo@x.com",
            Email = "demo@x.com",
            PlanHandle = "eshop-pro"
        });

        Assert.False(result.AlreadyExisted);
        Assert.Equal(901, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal("eshop-pro", result.Subscription.ProductHandle);

        // A customer was created, and the subscribe body used the plan handle + remittance (no card).
        Assert.Contains(handler.Requests, r => r.Method == "POST" && r.PathAndQuery.EndsWith("/customers.json"));
        var createSub = handler.Requests.Single(r => r.Method == "POST" && r.PathAndQuery.EndsWith("/subscriptions.json"));
        Assert.Contains("\"product_handle\":\"eshop-pro\"", createSub.Body);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", createSub.Body);
        Assert.Contains("\"customer_id\":555", createSub.Body);
    }

    [Fact]
    public async Task SubscribeAsync_IsIdempotent_WhenLiveSubscriptionExists()
    {
        var (service, handler) = BuildService((req, _) =>
        {
            var path = req.RequestUri!.PathAndQuery;
            if (req.Method == HttpMethod.Get && path.Contains("/product_families/"))
                return RecordingHttpMessageHandler.Json(HttpStatusCode.OK, ProductsJson);
            if (req.Method == HttpMethod.Get && path.Contains("/customers/lookup.json"))
                return RecordingHttpMessageHandler.Json(HttpStatusCode.OK, CustomerJson);
            if (req.Method == HttpMethod.Get && path.Contains("/subscriptions.json"))
                return RecordingHttpMessageHandler.Json(HttpStatusCode.OK, "[" + SubscriptionJson(901, "active", "eshop-pro") + "]");
            throw new InvalidOperationException($"Unexpected {req.Method} {path}");
        });

        var result = await service.SubscribeAsync(new SubscribeRequest
        {
            UserReference = "demo@x.com",
            PlanHandle = "eshop-pro"
        });

        Assert.True(result.AlreadyExisted);
        Assert.Equal(901, result.Subscription.Id);
        // No duplicate customer and no duplicate subscription were created.
        Assert.DoesNotContain(handler.Requests, r => r.Method == "POST" && r.PathAndQuery.EndsWith("/customers.json"));
        Assert.DoesNotContain(handler.Requests, r => r.Method == "POST" && r.PathAndQuery.EndsWith("/subscriptions.json"));
    }

    [Fact]
    public async Task SubscribeAsync_CreatesNew_WhenExistingSubscriptionIsCanceled()
    {
        var (service, handler) = BuildService((req, _) =>
        {
            var path = req.RequestUri!.PathAndQuery;
            if (req.Method == HttpMethod.Get && path.Contains("/product_families/"))
                return RecordingHttpMessageHandler.Json(HttpStatusCode.OK, ProductsJson);
            if (req.Method == HttpMethod.Get && path.Contains("/customers/lookup.json"))
                return RecordingHttpMessageHandler.Json(HttpStatusCode.OK, CustomerJson);
            if (req.Method == HttpMethod.Get && path.Contains("/subscriptions.json"))
                return RecordingHttpMessageHandler.Json(HttpStatusCode.OK, "[" + SubscriptionJson(901, "canceled", "eshop-pro") + "]");
            if (req.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json"))
                return RecordingHttpMessageHandler.Json(HttpStatusCode.Created, SubscriptionJson(902, "active", "eshop-pro"));
            throw new InvalidOperationException($"Unexpected {req.Method} {path}");
        });

        var result = await service.SubscribeAsync(new SubscribeRequest
        {
            UserReference = "demo@x.com",
            PlanHandle = "eshop-pro"
        });

        Assert.False(result.AlreadyExisted);
        Assert.Equal(902, result.Subscription.Id);
        Assert.Contains(handler.Requests, r => r.Method == "POST" && r.PathAndQuery.EndsWith("/subscriptions.json"));
    }

    [Fact]
    public async Task SubscribeAsync_Throws_WhenPlanHandleNotInFamily()
    {
        var (service, _) = BuildService((req, _) =>
            RecordingHttpMessageHandler.Json(HttpStatusCode.OK, ProductsJson));

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(() =>
            service.SubscribeAsync(new SubscribeRequest { UserReference = "demo@x.com", PlanHandle = "nope" }));
    }

    [Fact]
    public async Task GetSubscriptionsAsync_ReturnsEmpty_WhenNoCustomerExists()
    {
        var (service, handler) = BuildService((req, _) =>
            new HttpResponseMessage(HttpStatusCode.NotFound));

        var subs = await service.GetSubscriptionsAsync("unknown@x.com");

        Assert.Empty(subs);
        // Only the lookup was attempted — no customer subscriptions call.
        Assert.DoesNotContain(handler.Requests, r => r.PathAndQuery.Contains("/customers/") && r.PathAndQuery.EndsWith("/subscriptions.json"));
    }
}
