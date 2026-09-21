using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

public class MaxioSubscriptionBillingServiceTests
{
    private static readonly SubscriberIdentity Subscriber =
        new(userReference: "user1@example.com", email: "user1@example.com");

    // ----- helpers ------------------------------------------------------------------------------

    private sealed class RoutingStubHandler : HttpMessageHandler
    {
        private readonly System.Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<(HttpMethod Method, string Path)> Requests { get; } = new();

        public RoutingStubHandler(System.Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request.Method, request.RequestUri!.AbsolutePath));
            var response = _responder(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }

        public int Count(HttpMethod method, string pathContains) =>
            Requests.FindAll(r => r.Method == method && r.Path.Contains(pathContains)).Count;
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static (MaxioSubscriptionBillingService Service, RoutingStubHandler Handler) BuildService(
        System.Func<HttpRequestMessage, HttpResponseMessage> responder,
        MaxioOptions? options = null)
    {
        var handler = new RoutingStubHandler(responder);
        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials { Username = "key", Password = "x" },
            // No retries in tests so a POST failure surfaces immediately and request counts are exact.
            Retry = RetryOptions.Default() with { MaxRetries = 0, Timeout = System.TimeSpan.FromSeconds(5) },
        };
        clientOptions.Server.Production.Us.Site = "test";
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), clientOptions);

        options ??= new MaxioOptions
        {
            ApiKey = "key",
            Subdomain = "test",
            ProductFamilyHandle = "fam",
            PaymentCollectionMethod = "remittance",
        };

        var service = new MaxioSubscriptionBillingService(
            client, Options.Create(options), NullLogger<MaxioSubscriptionBillingService>.Instance);
        return (service, handler);
    }

    private const string ProPlanJson =
        """{"product":{"id":1,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false}}""";
    private const string NoHandleProductJson =
        """{"product":{"id":2,"name":"Broken","price_in_cents":100}}""";

    private static string SubscriptionJson(int id, string state = "active", string handle = "eshop-pro") =>
        "{\"subscription\":{\"id\":" + id + ",\"state\":\"" + state +
        "\",\"reference\":\"eshop:user1@example.com:eshop-pro\",\"product\":{\"handle\":\"" + handle +
        "\",\"name\":\"Pro Plan\"},\"product_price_in_cents\":29900," +
        "\"current_period_ends_at\":\"2026-10-01T00:00:00Z\",\"created_at\":\"2026-09-01T00:00:00Z\"}}";

    // ----- GetPlansAsync -------------------------------------------------------------------------

    [Fact]
    public async Task GetPlans_MapsProductsFromFamily()
    {
        var (service, _) = BuildService(_ => Json(HttpStatusCode.OK, $"[{ProPlanJson}]"));

        var plans = await service.GetPlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal("Pro Plan", plan.Name);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.Equal("month", plan.IntervalUnit);
        Assert.False(plan.RequiresPaymentMethod);
    }

    [Fact]
    public async Task GetPlans_SkipsProductsWithoutHandle()
    {
        var (service, _) = BuildService(_ => Json(HttpStatusCode.OK, $"[{ProPlanJson},{NoHandleProductJson}]"));

        var plans = await service.GetPlansAsync();

        Assert.Single(plans); // the handle-less product is skipped (cannot be subscribed to)
    }

    [Fact]
    public async Task GetPlans_ProviderError_ThrowsSubscriptionBillingException()
    {
        var (service, _) = BuildService(_ => Json(HttpStatusCode.InternalServerError, "boom"));

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(() => service.GetPlansAsync());
        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
    }

    // ----- SubscribeAsync ------------------------------------------------------------------------

    [Fact]
    public async Task Subscribe_UnknownPlan_ThrowsPlanNotFound()
    {
        var (service, _) = BuildService(_ => Json(HttpStatusCode.OK, $"[{ProPlanJson}]"));

        await Assert.ThrowsAsync<PlanNotFoundException>(() => service.SubscribeAsync(Subscriber, "no-such-plan"));
    }

    [Fact]
    public async Task Subscribe_CreatesSubscription_WhenNoneExists()
    {
        var (service, handler) = BuildService(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.Contains("/products.json")) return Json(HttpStatusCode.OK, $"[{ProPlanJson}]");
            if (path.Contains("/customers/lookup.json")) return Json(HttpStatusCode.OK, """{"customer":{"id":42,"reference":"user1@example.com"}}""");
            if (path.Contains("/subscriptions/lookup.json")) return Json(HttpStatusCode.NotFound, "{}"); // no existing sub
            if (req.Method == HttpMethod.Post && path.Contains("/subscriptions.json")) return Json(HttpStatusCode.Created, SubscriptionJson(101));
            return Json(HttpStatusCode.InternalServerError, "unexpected");
        });

        var result = await service.SubscribeAsync(Subscriber, "eshop-pro");

        Assert.False(result.AlreadyExisted);
        Assert.Equal(101, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal(1, handler.Count(HttpMethod.Post, "/subscriptions.json")); // exactly one create
    }

    [Fact]
    public async Task Subscribe_ReturnsExisting_WithoutCreating_WhenAlreadySubscribed()
    {
        var (service, handler) = BuildService(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.Contains("/products.json")) return Json(HttpStatusCode.OK, $"[{ProPlanJson}]");
            if (path.Contains("/customers/lookup.json")) return Json(HttpStatusCode.OK, """{"customer":{"id":42,"reference":"user1@example.com"}}""");
            if (path.Contains("/subscriptions/lookup.json")) return Json(HttpStatusCode.OK, SubscriptionJson(200)); // already exists
            return Json(HttpStatusCode.InternalServerError, "unexpected");
        });

        var result = await service.SubscribeAsync(Subscriber, "eshop-pro");

        Assert.True(result.AlreadyExisted);
        Assert.Equal(200, result.Subscription.Id);
        Assert.Equal(0, handler.Count(HttpMethod.Post, "/subscriptions.json")); // idempotent: no create issued
    }

    [Fact]
    public async Task Subscribe_CreatesCustomer_WhenMissing()
    {
        var (service, handler) = BuildService(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.Contains("/products.json")) return Json(HttpStatusCode.OK, $"[{ProPlanJson}]");
            if (req.Method == HttpMethod.Get && path.Contains("/customers/lookup.json")) return Json(HttpStatusCode.NotFound, "{}"); // customer absent
            if (req.Method == HttpMethod.Post && path.Contains("/customers.json")) return Json(HttpStatusCode.Created, """{"customer":{"id":77,"reference":"user1@example.com"}}""");
            if (path.Contains("/subscriptions/lookup.json")) return Json(HttpStatusCode.NotFound, "{}");
            if (req.Method == HttpMethod.Post && path.Contains("/subscriptions.json")) return Json(HttpStatusCode.Created, SubscriptionJson(101));
            return Json(HttpStatusCode.InternalServerError, "unexpected");
        });

        var result = await service.SubscribeAsync(Subscriber, "eshop-pro");

        Assert.False(result.AlreadyExisted);
        Assert.Equal(1, handler.Count(HttpMethod.Post, "/customers.json")); // customer was created
    }

    [Fact]
    public async Task Subscribe_ReconcilesCustomer_OnDuplicateCreateConflict()
    {
        // Simulate a concurrent create: lookup first misses, create is rejected (reference taken),
        // and the reconciling re-read then finds the customer created by the racing request.
        var lookupCalls = 0;
        var (service, handler) = BuildService(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.Contains("/products.json")) return Json(HttpStatusCode.OK, $"[{ProPlanJson}]");
            if (req.Method == HttpMethod.Get && path.Contains("/customers/lookup.json"))
            {
                lookupCalls++;
                return lookupCalls == 1
                    ? Json(HttpStatusCode.NotFound, "{}")                                              // first: absent
                    : Json(HttpStatusCode.OK, """{"customer":{"id":88,"reference":"user1@example.com"}}"""); // reconcile: present
            }
            if (req.Method == HttpMethod.Post && path.Contains("/customers.json"))
                return Json(HttpStatusCode.UnprocessableEntity, """{"errors":{"reference":["has already been taken"]}}""");
            if (path.Contains("/subscriptions/lookup.json")) return Json(HttpStatusCode.NotFound, "{}");
            if (req.Method == HttpMethod.Post && path.Contains("/subscriptions.json")) return Json(HttpStatusCode.Created, SubscriptionJson(101));
            return Json(HttpStatusCode.InternalServerError, "unexpected");
        });

        var result = await service.SubscribeAsync(Subscriber, "eshop-pro");

        Assert.False(result.AlreadyExisted);
        Assert.Equal(101, result.Subscription.Id); // proceeded to subscribe using the reconciled customer
    }

    [Fact]
    public async Task Subscribe_ReconcilesSubscription_OnCreateTransportFailure()
    {
        // The create POST fails at the transport level (unknown outcome); the reconciling re-read then
        // finds the subscription, so the service reports success rather than a spurious failure.
        var subLookupCalls = 0;
        var (service, _) = BuildService(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.Contains("/products.json")) return Json(HttpStatusCode.OK, $"[{ProPlanJson}]");
            if (path.Contains("/customers/lookup.json")) return Json(HttpStatusCode.OK, """{"customer":{"id":42,"reference":"user1@example.com"}}""");
            if (req.Method == HttpMethod.Get && path.Contains("/subscriptions/lookup.json"))
            {
                subLookupCalls++;
                return subLookupCalls == 1
                    ? Json(HttpStatusCode.NotFound, "{}")           // pre-create: none
                    : Json(HttpStatusCode.OK, SubscriptionJson(202)); // reconcile: found
            }
            if (req.Method == HttpMethod.Post && path.Contains("/subscriptions.json"))
                throw new HttpRequestException("connection reset");
            return Json(HttpStatusCode.InternalServerError, "unexpected");
        });

        var result = await service.SubscribeAsync(Subscriber, "eshop-pro");

        Assert.True(result.AlreadyExisted);
        Assert.Equal(202, result.Subscription.Id);
    }

    // ----- GetSubscriptionsAsync -----------------------------------------------------------------

    [Fact]
    public async Task GetSubscriptions_ReturnsEmpty_WhenNoCustomer()
    {
        var (service, handler) = BuildService(req =>
            req.RequestUri!.AbsolutePath.Contains("/customers/lookup.json")
                ? Json(HttpStatusCode.NotFound, "{}")
                : Json(HttpStatusCode.InternalServerError, "unexpected"));

        var subs = await service.GetSubscriptionsAsync(Subscriber);

        Assert.Empty(subs);
        Assert.Equal(0, handler.Count(HttpMethod.Get, "/subscriptions.json"));
    }

    [Fact]
    public async Task GetSubscriptions_MapsCustomerSubscriptions()
    {
        var (service, _) = BuildService(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.Contains("/customers/lookup.json")) return Json(HttpStatusCode.OK, """{"customer":{"id":42,"reference":"user1@example.com"}}""");
            if (path.Contains("/customers/42/subscriptions.json")) return Json(HttpStatusCode.OK, $"[{SubscriptionJson(300)}]");
            return Json(HttpStatusCode.InternalServerError, "unexpected");
        });

        var subs = await service.GetSubscriptionsAsync(Subscriber);

        var sub = Assert.Single(subs);
        Assert.Equal(300, sub.Id);
        Assert.Equal("eshop-pro", sub.PlanHandle);
        Assert.Equal("active", sub.State);
    }
}
