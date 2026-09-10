using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Maxio;

/// <summary>
/// Tests the Maxio integration by faking the SDK's <see cref="HttpClient"/> seam (a stub
/// <see cref="HttpMessageHandler"/>), so no real network calls happen. Asserts real behaviour: DTO mapping,
/// idempotent subscribe, customer-create race recovery, and error translation.
/// </summary>
public class MaxioSubscriptionBillingServiceTests
{
    private const string FamilyHandle = "eshop-subscribe";
    private const string PlanHandle = "eshop-pro";

    private static MaxioSubscriptionBillingService BuildService(StubHandler handler)
    {
        var options = new MaxioAdvancedBillingClientOptions { Environment = ServerEnvironment.Us };
        options.Server.Production.Us.Site = "test-subdomain";
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), options);

        var settings = Options.Create(new MaxioSettings
        {
            ApiKey = "k",
            Subdomain = "test-subdomain",
            ProductFamilyHandle = FamilyHandle
        });

        return new MaxioSubscriptionBillingService(client, settings, NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    private static SubscriberIdentity Subscriber() =>
        new("demouser@microsoft.com", "demouser@microsoft.com", "Demo", "User");

    // ── GetPlansAsync ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPlans_MapsNonArchivedProductsInTheFamily()
    {
        var handler = Handler(
            Get("/product_families.json", FamilyListJson),
            Get("/product_families/3023074/products.json", () => """
                [
                  {"product":{"id":1,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false}},
                  {"product":{"id":2,"name":"Old","handle":"old-plan","price_in_cents":100,"archived_at":"2020-01-01T00:00:00+00:00"}}
                ]
                """));

        var plans = await BuildService(handler).GetPlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal("Pro Plan", plan.Name);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.Equal(299.00m, plan.Price);
        Assert.Equal("month", plan.IntervalUnit);
        Assert.False(plan.RequiresPaymentMethod);
    }

    // ── SubscribeAsync ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Subscribe_CreatesSubscription_WhenNoneExists()
    {
        var handler = Handler(
            Get("/product_families.json", FamilyListJson),
            Get("/product_families/3023074/products.json", ProductListJson),
            Get("/customers/lookup.json", () => (HttpStatusCode.NotFound, """{"error":"not found"}""")),
            Post("/customers.json", () => (HttpStatusCode.Created, """{"customer":{"id":555}}""")),
            Get("/customers/555/subscriptions.json", () => "[]"),
            Post("/subscriptions.json", () => (HttpStatusCode.Created,
                """{"subscription":{"id":901,"state":"active","product":{"handle":"eshop-pro","name":"Pro Plan"},"product_price_in_cents":29900,"next_assessment_at":"2026-10-10T00:00:00+00:00"}}""")));

        var result = await BuildService(handler).SubscribeAsync(Subscriber(), PlanHandle);

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(901, result.SubscriptionId);
        Assert.Equal(555, result.CustomerId);
        Assert.Equal("eshop-pro", result.PlanHandle);
        Assert.Equal("active", result.State);
        Assert.NotNull(result.NextBillingDate);
        Assert.Equal(1, handler.CountPost("/subscriptions.json"));
    }

    [Fact]
    public async Task Subscribe_IsIdempotent_WhenLiveSubscriptionExists()
    {
        var handler = Handler(
            Get("/product_families.json", FamilyListJson),
            Get("/product_families/3023074/products.json", ProductListJson),
            Get("/customers/lookup.json", () => """{"customer":{"id":555,"reference":"demouser@microsoft.com"}}"""),
            Get("/customers/555/subscriptions.json", () =>
                """[{"subscription":{"id":900,"state":"active","product":{"handle":"eshop-pro","name":"Pro Plan"},"product_price_in_cents":29900,"next_assessment_at":"2026-10-10T00:00:00+00:00"}}]"""));

        var result = await BuildService(handler).SubscribeAsync(Subscriber(), PlanHandle);

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(900, result.SubscriptionId);
        Assert.Equal(0, handler.CountPost("/subscriptions.json"));   // no new subscription created
    }

    [Fact]
    public async Task Subscribe_RecoversFromDuplicateCustomerRace()
    {
        var lookupCalls = 0;
        var handler = new StubHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (req.Method == HttpMethod.Get && path == "/product_families.json") return Ok(FamilyListJson());
            if (req.Method == HttpMethod.Get && path == "/product_families/3023074/products.json") return Ok(ProductListJson());
            if (req.Method == HttpMethod.Get && path == "/customers/lookup.json")
            {
                // First lookup misses; after the (racing) create returns 422, the retry finds the customer.
                lookupCalls++;
                return lookupCalls == 1
                    ? Response(HttpStatusCode.NotFound, """{"error":"not found"}""")
                    : Ok("""{"customer":{"id":555,"reference":"demouser@microsoft.com"}}""");
            }
            if (req.Method == HttpMethod.Post && path == "/customers.json")
                return Response(HttpStatusCode.UnprocessableEntity, """{"errors":["Reference: has already been taken"]}""");
            if (req.Method == HttpMethod.Get && path == "/customers/555/subscriptions.json")
                return Ok("[]");
            if (req.Method == HttpMethod.Post && path == "/subscriptions.json")
                return Response(HttpStatusCode.Created, """{"subscription":{"id":902,"state":"active","product":{"handle":"eshop-pro"}}}""");
            return Response(HttpStatusCode.NotFound, "{}");
        });

        var result = await BuildService(handler).SubscribeAsync(Subscriber(), PlanHandle);

        Assert.Equal(555, result.CustomerId);
        Assert.Equal(902, result.SubscriptionId);
        Assert.Equal(2, lookupCalls);   // proves the 422 duplicate was recovered by re-reading
    }

    [Fact]
    public async Task Subscribe_UnknownPlan_IsCallerError()
    {
        var handler = Handler(
            Get("/product_families.json", FamilyListJson),
            Get("/product_families/3023074/products.json", ProductListJson));

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => BuildService(handler).SubscribeAsync(Subscriber(), "does-not-exist"));

        Assert.True(ex.IsCallerError);
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Subscribe_TranslatesSubscriptionValidationError()
    {
        var handler = Handler(
            Get("/product_families.json", FamilyListJson),
            Get("/product_families/3023074/products.json", ProductListJson),
            Get("/customers/lookup.json", () => """{"customer":{"id":555}}"""),
            Get("/customers/555/subscriptions.json", () => "[]"),
            Post("/subscriptions.json", () => (HttpStatusCode.UnprocessableEntity, """{"errors":["Product handle: is not valid"]}""")));

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => BuildService(handler).SubscribeAsync(Subscriber(), PlanHandle));

        Assert.True(ex.IsCallerError);
        Assert.Equal(422, ex.StatusCode);
        Assert.Contains("Product handle", ex.Message);
    }

    // ── GetSubscriptionsAsync ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSubscriptions_ReturnsEmpty_WhenCustomerDoesNotExist()
    {
        var handler = Handler(
            Get("/customers/lookup.json", () => (HttpStatusCode.NotFound, """{"error":"not found"}""")));

        var subs = await BuildService(handler).GetSubscriptionsAsync("nobody@example.com");

        Assert.Empty(subs);
    }

    [Fact]
    public async Task GetSubscriptions_MapsCustomerSubscriptions()
    {
        var handler = Handler(
            Get("/customers/lookup.json", () => """{"customer":{"id":555}}"""),
            Get("/customers/555/subscriptions.json", () =>
                """[{"subscription":{"id":900,"state":"active","product":{"handle":"eshop-pro","name":"Pro Plan"},"product_price_in_cents":29900,"current_period_ends_at":"2026-10-10T00:00:00+00:00"}}]"""));

        var subs = await BuildService(handler).GetSubscriptionsAsync("demouser@microsoft.com");

        var sub = Assert.Single(subs);
        Assert.Equal(900, sub.Id);
        Assert.Equal("eshop-pro", sub.PlanHandle);
        Assert.Equal("active", sub.State);
        Assert.Equal(29900, sub.PriceInCents);
    }

    // ── Fixtures / stub ───────────────────────────────────────────────────────────────────────────────

    private static string FamilyListJson() =>
        """[{"product_family":{"id":3023074,"handle":"eshop-subscribe","name":"eShop"}}]""";

    private static string ProductListJson() =>
        """[{"product":{"id":1,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false}}]""";

    private static HttpResponseMessage Ok(string json) => Response(HttpStatusCode.OK, json);

    private static HttpResponseMessage Response(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    // A route whose factory returns just a JSON body (200 OK).
    private static Route Get(string path, Func<string> okJson) =>
        new(HttpMethod.Get, path, () => Ok(okJson()));

    // A route whose factory returns a (status, json) pair.
    private static Route Get(string path, Func<(HttpStatusCode, string)> make) =>
        new(HttpMethod.Get, path, () => { var (s, j) = make(); return Response(s, j); });

    private static Route Post(string path, Func<(HttpStatusCode, string)> make) =>
        new(HttpMethod.Post, path, () => { var (s, j) = make(); return Response(s, j); });

    private static StubHandler Handler(params Route[] routes) => new(req =>
    {
        foreach (var route in routes)
        {
            if (req.Method == route.Method && req.RequestUri!.AbsolutePath == route.Path)
            {
                return route.Make();
            }
        }
        return Response(HttpStatusCode.NotFound, """{"error":"unrouted"}""");
    });

    private sealed record Route(HttpMethod Method, string Path, Func<HttpResponseMessage> Make);

    public sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<HttpRequestMessage> Requests { get; } = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        public int CountPost(string absolutePath) =>
            Requests.Count(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath == absolutePath);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var response = _responder(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }
}
