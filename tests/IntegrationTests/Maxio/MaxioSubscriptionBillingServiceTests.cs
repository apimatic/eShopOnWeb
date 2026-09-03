using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Maxio;

/// <summary>
/// Exercises <see cref="MaxioSubscriptionBillingService"/> against a stubbed HttpMessageHandler
/// (the SDK's testing seam) so behaviour — mapping, idempotency, and failure translation — is
/// verified without live Maxio connectivity. Paths match the Maxio Advanced Billing wire routes.
/// </summary>
public class MaxioSubscriptionBillingServiceTests
{
    // A fictitious handle: the test is fully stubbed, so the real configured family handle is not
    // needed here (and real credential/config values are kept out of the repo).
    private const string FamilyHandle = "test-product-family";
    private const string ProPlan = "test-pro-plan";

    // --- test seam ---------------------------------------------------------------------------

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string, HttpResponseMessage> _responder;
        public List<(HttpMethod Method, string Path, string Body)> Requests { get; } = new();

        public StubHandler(Func<HttpRequestMessage, string, HttpResponseMessage> responder) => _responder = responder;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method, request.RequestUri!.AbsolutePath, body));
            return _responder(request, body);
        }

        public int PostCount(string pathEndsWith) =>
            Requests.Count(r => r.Method == HttpMethod.Post && r.Path.EndsWith(pathEndsWith, StringComparison.Ordinal));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static MaxioSubscriptionBillingService BuildService(StubHandler handler)
    {
        var options = new MaxioAdvancedBillingClientOptions();
        options.Server.Production.Us.BaseUrl = "https://test.chargify.com";
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), options);
        var settings = Options.Create(new MaxioSettings { ProductFamilyHandle = FamilyHandle });
        var logger = Substitute.For<IAppLogger<MaxioSubscriptionBillingService>>();
        return new MaxioSubscriptionBillingService(client, settings, logger);
    }

    private static string FamiliesJson() =>
        $"[{{\"product_family\":{{\"id\":123,\"handle\":\"{FamilyHandle}\",\"name\":\"eShop Subscribe\"}}}}]";

    private static string ProductsJson() =>
        "[" +
        "{\"product\":{\"id\":1,\"handle\":\"basic-plan\",\"name\":\"Basic Plan\",\"price_in_cents\":2900,\"interval\":1,\"interval_unit\":\"month\"}}," +
        $"{{\"product\":{{\"id\":2,\"handle\":\"{ProPlan}\",\"name\":\"Pro Plan\",\"price_in_cents\":29900,\"interval\":1,\"interval_unit\":\"month\"}}}}" +
        "]";

    private static string CustomerJson(int id) =>
        $"{{\"customer\":{{\"id\":{id},\"reference\":\"demo\",\"email\":\"demo@x.test\",\"first_name\":\"demo\",\"last_name\":\"eShopOnWeb\"}}}}";

    private static string SubscriptionJson(int id, string handle, string state) =>
        $"{{\"subscription\":{{\"id\":{id},\"state\":\"{state}\",\"product_price_in_cents\":29900," +
        $"\"next_assessment_at\":\"2026-10-03T00:00:00Z\",\"current_period_ends_at\":\"2026-10-03T00:00:00Z\"," +
        $"\"product\":{{\"handle\":\"{handle}\",\"name\":\"Pro Plan\"}}}}}}";

    // Routes a request to the right canned response; `subscriptionsList` lets each test choose
    // whether the customer already has subscriptions.
    private static HttpResponseMessage Route(HttpRequestMessage req, string subscriptionsListJson, Func<HttpResponseMessage> onCreateSubscription)
    {
        var path = req.RequestUri!.AbsolutePath;
        if (req.Method == HttpMethod.Get && path.EndsWith("/product_families.json", StringComparison.Ordinal))
            return Json(HttpStatusCode.OK, FamiliesJson());
        if (req.Method == HttpMethod.Get && path.EndsWith("/products.json", StringComparison.Ordinal))
            return Json(HttpStatusCode.OK, ProductsJson());
        if (req.Method == HttpMethod.Get && path.EndsWith("/customers/lookup.json", StringComparison.Ordinal))
            return Json(HttpStatusCode.OK, CustomerJson(55));
        if (req.Method == HttpMethod.Get && path.EndsWith("/subscriptions.json", StringComparison.Ordinal))
            return Json(HttpStatusCode.OK, subscriptionsListJson);
        if (req.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json", StringComparison.Ordinal))
            return onCreateSubscription();
        if (req.Method == HttpMethod.Post && path.EndsWith("/customers.json", StringComparison.Ordinal))
            return Json(HttpStatusCode.OK, CustomerJson(55));
        return Json(HttpStatusCode.NotFound, "{}");
    }

    // --- tests -------------------------------------------------------------------------------

    [Fact]
    public async Task GetPlansAsync_ResolvesFamilyByHandle_AndMapsCentsToAmount()
    {
        var handler = new StubHandler((req, _) => Route(req, "[]", () => Json(HttpStatusCode.OK, "{}")));
        var service = BuildService(handler);

        var plans = await service.GetPlansAsync();

        Assert.Equal(2, plans.Count);
        var pro = plans.Single(p => p.Handle == ProPlan);
        Assert.Equal(299m, pro.Price);
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal("month", pro.IntervalUnit);
        Assert.Equal(1, pro.Interval);
        // Family was resolved by handle to numeric id 123 before listing products.
        Assert.Contains(handler.Requests, r => r.Path.EndsWith("/product_families/123/products.json", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SubscribeAsync_WhenLiveSubscriptionExists_DoesNotCreateAnother()
    {
        var existing = "[" + SubscriptionJson(900, ProPlan, "active") + "]";
        var handler = new StubHandler((req, _) => Route(req, existing, () => Json(HttpStatusCode.OK, SubscriptionJson(901, ProPlan, "active"))));
        var service = BuildService(handler);

        var result = await service.SubscribeAsync(SubscriberIdentity.FromUserName("demo@x.test"), ProPlan);

        Assert.Equal(900, result.Id);                       // the existing subscription, not a new one
        Assert.Equal(0, handler.PostCount("/subscriptions.json"));  // write-once: no create issued
    }

    [Fact]
    public async Task SubscribeAsync_WhenNoSubscription_CreatesWithRemittanceCollection()
    {
        var handler = new StubHandler((req, _) => Route(req, "[]", () => Json(HttpStatusCode.OK, SubscriptionJson(901, ProPlan, "active"))));
        var service = BuildService(handler);

        var result = await service.SubscribeAsync(SubscriberIdentity.FromUserName("demo@x.test"), ProPlan);

        Assert.Equal(901, result.Id);
        Assert.Equal("active", result.State);
        Assert.Equal(299m, result.Price);
        Assert.Equal(1, handler.PostCount("/subscriptions.json"));

        var createBody = handler.Requests.Single(r => r.Method == HttpMethod.Post && r.Path.EndsWith("/subscriptions.json", StringComparison.Ordinal)).Body;
        Assert.Contains("\"payment_collection_method\":\"remittance\"", createBody);
        Assert.Contains($"\"product_handle\":\"{ProPlan}\"", createBody);
        Assert.Contains("\"customer_reference\":\"demo@x.test\"", createBody);
    }

    [Fact]
    public async Task SubscribeAsync_WhenProviderRejects422_ThrowsBillingValidationWithMessage()
    {
        var handler = new StubHandler((req, _) => Route(req, "[]",
            () => Json((HttpStatusCode)422, "{\"errors\":[\"No payment method was on file for the $299.00 balance\"]}")));
        var service = BuildService(handler);

        var ex = await Assert.ThrowsAsync<BillingValidationException>(
            () => service.SubscribeAsync(SubscriberIdentity.FromUserName("demo@x.test"), ProPlan));

        Assert.Equal(422, ex.StatusCode);
        Assert.Contains("No payment method was on file", ex.Message);
    }

    [Fact]
    public async Task SubscribeAsync_WhenPlanHandleUnknown_ThrowsPlanNotFound()
    {
        var handler = new StubHandler((req, _) => Route(req, "[]", () => Json(HttpStatusCode.OK, SubscriptionJson(901, ProPlan, "active"))));
        var service = BuildService(handler);

        var ex = await Assert.ThrowsAsync<PlanNotFoundException>(
            () => service.SubscribeAsync(SubscriberIdentity.FromUserName("demo@x.test"), "no-such-plan"));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(0, handler.PostCount("/subscriptions.json"));   // never reached the provider
    }
}
