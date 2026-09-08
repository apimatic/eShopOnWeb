using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

/// <summary>
/// Tests the Maxio billing integration over the SDK's HttpClient seam: no real
/// network calls, request/response pairs are stubbed per path.
/// </summary>
public class MaxioBillingServiceTests
{
    private const string CustomerJson = @"{""customer"":{""id"":123,""reference"":""u1"",""email"":""u1@eshop.com""}}";

    private const string ProPlanJson = @"{""product"":{""handle"":""eshop-pro"",""name"":""Pro Plan"",
""price_in_cents"":29900,""interval"":1,""interval_unit"":""month"",""product_family"":{""handle"":""eshop-subscribe""}}}";

    private const string SubscriptionJson = @"{""subscription"":{""id"":555,""state"":""active"",
""product_price_in_cents"":29900,""current_period_ends_at"":""2026-10-09T00:00:00Z"",
""next_assessment_at"":""2026-10-09T00:00:00Z"",""reference"":""u1:eshop-pro"",
""product"":{""handle"":""eshop-pro"",""name"":""Pro Plan"",""price_in_cents"":29900,
""interval"":1,""interval_unit"":""month""}}}";

    private sealed class RoutingHandler : HttpMessageHandler
    {
        private readonly List<Func<HttpRequestMessage, HttpResponseMessage?>> _rules = new();

        public List<HttpRequestMessage> Requests { get; } = new();

        /// <summary>POST bodies captured at send time (request content is disposed after send).</summary>
        public List<(string Path, string Body)> Posts { get; } = new();

        public void AddRule(Func<HttpRequestMessage, HttpResponseMessage?> rule) => _rules.Add(rule);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (request.Method == HttpMethod.Post && request.Content != null)
            {
                var body = request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                Posts.Add((request.RequestUri!.AbsolutePath, body));
            }

            foreach (var rule in _rules)
            {
                var response = rule(request);
                if (response != null) return Task.FromResult(response);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent(string.Empty)
            });
        }
    }

    private static MaxioOptions DefaultOptions() => new MaxioOptions
    {
        ApiKey = "test-api-key",
        Subdomain = "test-site",
        ProductFamilyHandle = "eshop-subscribe",
        DefaultPlanHandle = "eshop-pro"
    };

    private static (MaxioBillingService Service, RoutingHandler Handler) CreateService(
        MaxioOptions? options = null)
    {
        var handler = new RoutingHandler();
        var service = new MaxioBillingService(new HttpClient(handler),
            Options.Create(options ?? DefaultOptions()), NullLogger<MaxioBillingService>.Instance);
        return (service, handler);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static HttpResponseMessage Empty(HttpStatusCode status) =>
        new HttpResponseMessage(status) { Content = new StringContent(string.Empty) };

    private static bool IsGet(HttpRequestMessage request, string path, string? queryContains = null) =>
        request.Method == HttpMethod.Get &&
        request.RequestUri!.AbsolutePath.EndsWith(path, StringComparison.Ordinal) &&
        (queryContains == null || request.RequestUri.Query.Contains(queryContains));

    private static bool IsPost(HttpRequestMessage request, string path) =>
        request.Method == HttpMethod.Post &&
        request.RequestUri!.AbsolutePath.EndsWith(path, StringComparison.Ordinal);

    [Fact]
    public async Task SubscribeFirstTimeCreatesCustomerAndSubscription()
    {
        var (service, handler) = CreateService();
        handler.AddRule(r => IsGet(r, "/products/handle/eshop-pro.json")
            ? Json(HttpStatusCode.OK, ProPlanJson) : null);
        handler.AddRule(r => IsGet(r, "/customers/lookup.json", "reference=u1")
            ? Empty(HttpStatusCode.NotFound) : null);
        handler.AddRule(r => IsPost(r, "/customers.json")
            ? Json(HttpStatusCode.Created, CustomerJson) : null);
        handler.AddRule(r => IsGet(r, "/subscriptions/lookup.json")
            ? Empty(HttpStatusCode.NotFound) : null);
        handler.AddRule(r => IsPost(r, "/subscriptions.json")
            ? Json(HttpStatusCode.Created, SubscriptionJson) : null);

        var result = await service.SubscribeAsync("u1", "u1@eshop.com", "demouser", null, CancellationToken.None);

        Assert.True(result.Created);
        Assert.Equal(123, result.BillingCustomerId);
        Assert.Equal(555, result.Subscription.SubscriptionId);
        Assert.Equal("eshop-pro", result.Subscription.PlanHandle);
        Assert.Equal("active", result.Subscription.State);
        Assert.True(result.Subscription.IsActive);
        Assert.Equal(29900, result.Subscription.PriceInCents);
        Assert.NotNull(result.Subscription.NextBillingDate);

        // The subscription create carries plan + customer + idempotent reference, no payment fields.
        var createBody = handler.Posts.Single(p => p.Path.EndsWith("/subscriptions.json", StringComparison.Ordinal)).Body;
        Assert.Contains("\"product_handle\":\"eshop-pro\"", createBody);
        Assert.Contains("\"customer_id\":123", createBody);
        Assert.Contains("\"reference\":\"u1:eshop-pro\"", createBody);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", createBody);
        Assert.DoesNotContain("payment_profile", createBody);
        Assert.DoesNotContain("credit_card", createBody);
    }

    [Fact]
    public async Task SubscribeFallsBackToInvoiceCollectionOnLegacySite()
    {
        var (service, handler) = CreateService();
        handler.AddRule(r => IsGet(r, "/products/handle/eshop-pro.json")
            ? Json(HttpStatusCode.OK, ProPlanJson) : null);
        handler.AddRule(r => IsGet(r, "/customers/lookup.json", "reference=u1")
            ? Json(HttpStatusCode.OK, CustomerJson) : null);
        handler.AddRule(r => IsGet(r, "/subscriptions/lookup.json")
            ? Empty(HttpStatusCode.NotFound) : null);
        var createCalls = 0;
        handler.AddRule(r => IsPost(r, "/subscriptions.json")
            ? ++createCalls == 1
                ? Json(HttpStatusCode.UnprocessableEntity,
                    @"{""errors"":[""No payment method was on file for the $299.00 balance""]}")
                : Json(HttpStatusCode.Created, SubscriptionJson)
            : null);

        var result = await service.SubscribeAsync("u1", "u1@eshop.com", "demouser", null, CancellationToken.None);

        Assert.True(result.Created);
        Assert.Equal(555, result.Subscription.SubscriptionId);
        Assert.Equal(2, handler.Posts.Count(p => p.Path.EndsWith("/subscriptions.json", StringComparison.Ordinal)));
        Assert.Contains("\"payment_collection_method\":\"invoice\"",
            handler.Posts.Last(p => p.Path.EndsWith("/subscriptions.json", StringComparison.Ordinal)).Body);
    }

    [Fact]
    public async Task SubscribeIsIdempotentOnRepeat()
    {
        var (service, handler) = CreateService();
        handler.AddRule(r => IsGet(r, "/products/handle/eshop-pro.json")
            ? Json(HttpStatusCode.OK, ProPlanJson) : null);
        handler.AddRule(r => IsGet(r, "/customers/lookup.json", "reference=u1")
            ? Json(HttpStatusCode.OK, CustomerJson) : null);
        handler.AddRule(r => IsGet(r, "/subscriptions/lookup.json")
            ? Json(HttpStatusCode.OK, SubscriptionJson) : null);

        var result = await service.SubscribeAsync("u1", "u1@eshop.com", "demouser", null, CancellationToken.None);

        Assert.False(result.Created);
        Assert.Equal(555, result.Subscription.SubscriptionId);
        Assert.DoesNotContain(handler.Requests, r => IsPost(r, "/subscriptions.json"));
        Assert.DoesNotContain(handler.Requests, r => IsPost(r, "/customers.json"));
    }

    [Fact]
    public async Task SubscribeWithUnknownPlanIsRejected()
    {
        var (service, handler) = CreateService();
        handler.AddRule(r => IsGet(r, "/products/handle/nope.json")
            ? Empty(HttpStatusCode.NotFound) : null);

        var ex = await Assert.ThrowsAsync<MaxioBillingException>(() =>
            service.SubscribeAsync("u1", "u1@eshop.com", "demouser", "nope", CancellationToken.None));

        Assert.Equal(MaxioBillingErrorKind.Rejected, ex.Kind);
        Assert.Equal(404, ex.ProviderStatusCode);
    }

    [Fact]
    public async Task SubscribeSurfacesProvider422AsRejection()
    {
        var (service, handler) = CreateService();
        handler.AddRule(r => IsGet(r, "/products/handle/eshop-pro.json")
            ? Json(HttpStatusCode.OK, ProPlanJson) : null);
        handler.AddRule(r => IsGet(r, "/customers/lookup.json", "reference=u1")
            ? Json(HttpStatusCode.OK, CustomerJson) : null);
        handler.AddRule(r => IsGet(r, "/subscriptions/lookup.json")
            ? Empty(HttpStatusCode.NotFound) : null);
        handler.AddRule(r => IsPost(r, "/subscriptions.json")
            ? Json(HttpStatusCode.UnprocessableEntity, @"{""errors"":[""Plan cannot be purchased""]}") : null);

        var ex = await Assert.ThrowsAsync<MaxioBillingException>(() =>
            service.SubscribeAsync("u1", "u1@eshop.com", "demouser", null, CancellationToken.None));

        Assert.Equal(MaxioBillingErrorKind.Rejected, ex.Kind);
        Assert.Equal(422, ex.ProviderStatusCode);
        Assert.Contains("Plan cannot be purchased", ex.Message);
    }

    [Fact]
    public async Task SubscribeWithMalformedSuccessBodyIsUnparseable()
    {
        var (service, handler) = CreateService();
        handler.AddRule(r => IsGet(r, "/products/handle/eshop-pro.json")
            ? Json(HttpStatusCode.OK, "{not-json") : null);

        var ex = await Assert.ThrowsAsync<MaxioBillingException>(() =>
            service.SubscribeAsync("u1", "u1@eshop.com", "demouser", null, CancellationToken.None));

        Assert.Equal(MaxioBillingErrorKind.Unparseable, ex.Kind);
    }

    [Fact]
    public async Task SubscribeAfterTransportFailureReconcilesByReference()
    {
        var (service, handler) = CreateService();
        handler.AddRule(r => IsGet(r, "/products/handle/eshop-pro.json")
            ? Json(HttpStatusCode.OK, ProPlanJson) : null);
        handler.AddRule(r => IsGet(r, "/customers/lookup.json", "reference=u1")
            ? Json(HttpStatusCode.OK, CustomerJson) : null);
        // First lookup misses; after the failed create the reconcile lookup finds it.
        var subscriptionLookupCalls = 0;
        handler.AddRule(r => IsGet(r, "/subscriptions/lookup.json")
            ? (++subscriptionLookupCalls == 1
                ? Empty(HttpStatusCode.NotFound)
                : Json(HttpStatusCode.OK, SubscriptionJson)) : null);
        handler.AddRule(r => IsPost(r, "/subscriptions.json")
            ? throw new HttpRequestException("connection reset") : null);

        var result = await service.SubscribeAsync("u1", "u1@eshop.com", "demouser", null, CancellationToken.None);

        // The retried writes all failed, but the reconcile-by-reference found the subscription.
        Assert.True(result.Created);
        Assert.Equal(555, result.Subscription.SubscriptionId);
        Assert.True(handler.Requests.Count(IsPostWith("/subscriptions.json")) >= 1);
    }

    [Fact]
    public async Task ListPlansReturnsPlansInTheConfiguredFamily()
    {
        var (service, handler) = CreateService();
        handler.AddRule(r => IsGet(r, "/product_families.json")
            ? Json(HttpStatusCode.OK,
                @"[{""product_family"":{""id"":1,""handle"":""eshop-subscribe"",""name"":""eShop""}}]") : null);
        handler.AddRule(r => IsGet(r, "/product_families/1/products.json")
            ? Json(HttpStatusCode.OK,
                "[" + ProPlanJson +
                @",{""product"":{""handle"":""basic-plan"",""name"":""Basic Plan"",""price_in_cents"":2900,
""interval"":1,""interval_unit"":""month"",""product_family"":{""handle"":""eshop-subscribe""}}}]") : null);

        var plans = await service.ListPlansAsync(CancellationToken.None);

        Assert.Equal(2, plans.Count);
        var pro = plans.Single(p => p.Handle == "eshop-pro");
        Assert.True(pro.IsDefault);
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal("month", pro.IntervalUnit);
        Assert.False(plans.Single(p => p.Handle == "basic-plan").IsDefault);
    }

    [Fact]
    public async Task ListPlansWithoutConfiguredFamilyIsRejected()
    {
        var (service, handler) = CreateService();
        handler.AddRule(r => IsGet(r, "/product_families.json")
            ? Json(HttpStatusCode.OK, @"[]") : null);

        var ex = await Assert.ThrowsAsync<MaxioBillingException>(() =>
            service.ListPlansAsync(CancellationToken.None));

        Assert.Equal(MaxioBillingErrorKind.Rejected, ex.Kind);
        Assert.Equal(404, ex.ProviderStatusCode);
    }

    [Fact]
    public async Task MySubscriptionsReturnsEmptyListWhenNoBillingCustomer()
    {
        var (service, handler) = CreateService();
        handler.AddRule(r => IsGet(r, "/customers/lookup.json", "reference=u1")
            ? Empty(HttpStatusCode.NotFound) : null);

        var subscriptions = await service.GetUserSubscriptionsAsync("u1", CancellationToken.None);

        Assert.Empty(subscriptions);
        Assert.DoesNotContain(handler.Requests, r => r.RequestUri!.AbsolutePath.Contains("subscriptions"));
    }

    [Fact]
    public async Task MySubscriptionsListsTheCustomersSubscriptions()
    {
        var (service, handler) = CreateService();
        handler.AddRule(r => IsGet(r, "/customers/lookup.json", "reference=u1")
            ? Json(HttpStatusCode.OK, CustomerJson) : null);
        handler.AddRule(r => IsGet(r, "/customers/123/subscriptions.json")
            ? Json(HttpStatusCode.OK, "[" + SubscriptionJson + "]") : null);

        var subscriptions = await service.GetUserSubscriptionsAsync("u1", CancellationToken.None);

        var sub = Assert.Single(subscriptions);
        Assert.Equal(555, sub.SubscriptionId);
        Assert.Equal("eshop-pro", sub.PlanHandle);
        Assert.True(sub.IsActive);
    }

    private static Func<HttpRequestMessage, bool> IsPostWith(string path) => r => IsPost(r, path);
}
