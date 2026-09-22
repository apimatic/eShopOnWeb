using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionServiceTests
{
    private const string FamilyHandle = "eshop-subscribe";
    private const string ProPlan = "eshop-pro";

    private static string FamiliesJson =>
        """
        [ { "product_family": { "id": 3023074, "handle": "eshop-subscribe", "name": "eShop Subscribe" } } ]
        """;

    private static string ProductsJson =>
        """
        [
          { "product": { "id": 1, "name": "Pro Plan", "handle": "eshop-pro", "description": "Pro", "price_in_cents": 29900, "interval": 1, "interval_unit": "month" } },
          { "product": { "id": 2, "name": "Basic Plan", "handle": "basic-plan", "description": "Basic", "price_in_cents": 2900, "interval": 1, "interval_unit": "month" } }
        ]
        """;

    /// <summary>Answers the family-handle→id resolution (ListProductFamilies) and the plan listing.</summary>
    private static HttpResponseMessage? RoutePlanLookups(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (request.Method == HttpMethod.Get && path.EndsWith("/product_families.json", StringComparison.Ordinal))
            return Json(HttpStatusCode.OK, FamiliesJson);
        if (request.Method == HttpMethod.Get && path.EndsWith("/products.json", StringComparison.Ordinal))
            return Json(HttpStatusCode.OK, ProductsJson);
        return null;
    }

    private static MaxioSubscriptionService BuildService(Func<HttpRequestMessage, HttpResponseMessage> responder, out StubHttpMessageHandler handler)
    {
        handler = new StubHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler);
        var client = new MaxioAdvancedBillingClient(httpClient, new MaxioAdvancedBillingClientOptions());
        var settings = Options.Create(new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = FamilyHandle
        });
        return new MaxioSubscriptionService(client, settings, NullLogger<MaxioSubscriptionService>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static SubscribeRequest DemoRequest(string planHandle) => new()
    {
        UserReference = "demouser@microsoft.com",
        Email = "demouser@microsoft.com",
        FirstName = "demouser",
        LastName = "eShopOnWeb Shopper",
        PlanHandle = planHandle
    };

    [Fact]
    public async Task GetPlansAsync_MapsProductsToPlans()
    {
        var service = BuildService(req => RoutePlanLookups(req) ?? Json(HttpStatusCode.InternalServerError, "{}"), out _);

        var result = await service.GetPlansAsync();

        Assert.False(result.Truncated);
        Assert.Equal(2, result.Plans.Count);
        var pro = result.Plans.Single(p => p.Handle == ProPlan);
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal(1, pro.Interval);
        Assert.Equal("month", pro.IntervalUnit);
    }

    [Fact]
    public async Task SubscribeAsync_UnknownPlanHandle_ThrowsCallerError()
    {
        var service = BuildService(req => RoutePlanLookups(req) ?? Json(HttpStatusCode.InternalServerError, "{}"), out _);

        var ex = await Assert.ThrowsAsync<BillingProviderException>(
            () => service.SubscribeAsync(DemoRequest("does-not-exist")));

        Assert.True(ex.IsCallerError);
        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }

    [Fact]
    public async Task SubscribeAsync_NewCustomer_CreatesSubscription()
    {
        var service = BuildService(request =>
        {
            var routed = RoutePlanLookups(request);
            if (routed is not null) return routed;
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.EndsWith("/customers/lookup.json", StringComparison.Ordinal))
                return Json(HttpStatusCode.NotFound, "{}"); // no customer yet
            if (request.Method == HttpMethod.Post && path.EndsWith("/customers.json", StringComparison.Ordinal))
                return Json(HttpStatusCode.Created, """{ "customer": { "id": 555, "reference": "demouser@microsoft.com" } }""");
            if (request.Method == HttpMethod.Get && path.Contains("/customers/") && path.EndsWith("/subscriptions.json", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, "[]"); // no existing subscriptions
            if (request.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json", StringComparison.Ordinal))
                return Json(HttpStatusCode.Created,
                    """{ "subscription": { "id": 999, "state": "active", "product": { "handle": "eshop-pro", "name": "Pro Plan" }, "product_price_in_cents": 29900, "next_assessment_at": "2026-10-22T00:00:00Z" } }""");
            return Json(HttpStatusCode.InternalServerError, "{}");
        }, out var handler);

        var result = await service.SubscribeAsync(DemoRequest(ProPlan));

        Assert.False(result.AlreadyExisted);
        Assert.Equal(999, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal(ProPlan, result.Subscription.PlanHandle);
        Assert.Single(handler.Requests, r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/subscriptions.json", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SubscribeAsync_ExistingLiveSubscription_IsIdempotent_NoSecondCreate()
    {
        var service = BuildService(request =>
        {
            var routed = RoutePlanLookups(request);
            if (routed is not null) return routed;
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.EndsWith("/customers/lookup.json", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, """{ "customer": { "id": 555, "reference": "demouser@microsoft.com" } }""");
            if (request.Method == HttpMethod.Get && path.Contains("/customers/") && path.EndsWith("/subscriptions.json", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK,
                    """[ { "subscription": { "id": 999, "state": "active", "product": { "handle": "eshop-pro", "name": "Pro Plan" } } } ]""");
            return Json(HttpStatusCode.InternalServerError, "{}");
        }, out var handler);

        var result = await service.SubscribeAsync(DemoRequest(ProPlan));

        Assert.True(result.AlreadyExisted);
        Assert.Equal(999, result.Subscription.Id);
        // The idempotent hit must NOT issue a create.
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/subscriptions.json", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetMySubscriptionsAsync_NoCustomer_ReturnsEmpty()
    {
        var service = BuildService(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.EndsWith("/customers/lookup.json", StringComparison.Ordinal))
                return Json(HttpStatusCode.NotFound, "{}");
            return Json(HttpStatusCode.InternalServerError, "{}");
        }, out _);

        var result = await service.GetMySubscriptionsAsync("nobody@example.com");

        Assert.Empty(result);
    }
}
