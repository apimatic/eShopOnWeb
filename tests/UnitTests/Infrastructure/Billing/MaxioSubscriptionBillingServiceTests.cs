using System.Net;
using System.Text.RegularExpressions;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Services.Billing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioSubscriptionBillingServiceTests
{
    private const string FamilyHandle = "eshop-subscribe";

    private const string FamiliesJson =
        """[ { "product_family": { "id": 3023074, "name": "eShop Subscribe", "handle": "eshop-subscribe" } } ]""";

    private const string ProductsJson =
        """[ { "product": { "id": 7126957, "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900, "interval": 1, "interval_unit": "month", "archived_at": null } } ]""";

    private const string CustomerJson =
        """{ "customer": { "id": 4242, "reference": "user-1", "email": "demouser@microsoft.com", "first_name": "demouser", "last_name": "Customer" } }""";

    private const string SubscriptionJson =
        """{ "subscription": { "id": 9001, "state": "active", "reference": "user-1:eshop-pro", "product_price_in_cents": 29900, "current_period_ends_at": "2026-09-25T00:00:00Z", "product": { "id": 7126957, "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900, "interval": 1, "interval_unit": "month" } } }""";

    private static MaxioSubscriptionBillingService CreateService(StubHandler handler)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            Retry = RetryOptions.Default()
        };
        options.Server.Production.Us.Site = "test-site";

        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), options);
        var settings = Options.Create(new MaxioSettings { ProductFamilyHandle = FamilyHandle });
        return new MaxioSubscriptionBillingService(client, settings, NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    [Fact]
    public async Task ListPlansReturnsActiveProductsAndSkipsArchived()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/products.json"))
            {
                return Json("""
                    [
                      { "product": { "id": 7126957, "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900, "interval": 1, "interval_unit": "month", "archived_at": null } },
                      { "product": { "id": 7126999, "handle": "old-plan", "name": "Old Plan", "price_in_cents": 100, "interval": 1, "interval_unit": "month", "archived_at": "2026-01-01T00:00:00Z" } }
                    ]
                    """);
            }

            return Json(FamiliesJson);
        });

        var service = CreateService(handler);

        var plans = await service.ListPlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal("Pro Plan", plan.Name);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.Equal("month", plan.IntervalUnit);
    }

    [Fact]
    public async Task SubscribeCreatesCustomerWhenMissingThenCreatesSubscription()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/products.json")) return Json(ProductsJson);
            if (path.EndsWith("/customers/lookup.json"))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = JsonContent("{}") };
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/customers.json")) return Json(CustomerJson);
            if (request.Method == HttpMethod.Get && Regex.IsMatch(path, "/customers/\\d+/subscriptions\\.json$")) return Json("[]");
            if (request.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json")) return Json(SubscriptionJson);
            return Json(FamiliesJson);
        });

        var service = CreateService(handler);

        var subscription = await service.SubscribeAsync("user-1", "demouser@microsoft.com", null, null, "eshop-pro");

        Assert.Equal(9001, subscription.SubscriptionId);
        Assert.Equal("active", subscription.State);
        Assert.Equal("eshop-pro", subscription.ProductHandle);
        Assert.Equal(new DateTimeOffset(2026, 9, 25, 0, 0, 0, TimeSpan.Zero), subscription.NextBillingDate);
        Assert.Equal(1, handler.Requests.Count(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/customers.json")));
        Assert.Equal(1, handler.Requests.Count(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/subscriptions.json")));
    }

    [Fact]
    public async Task SubscribeShortCircuitsWhenLiveSubscriptionExists()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/products.json")) return Json(ProductsJson);
            if (path.EndsWith("/customers/lookup.json")) return Json(CustomerJson);
            if (request.Method == HttpMethod.Get && Regex.IsMatch(path, "/customers/\\d+/subscriptions\\.json$"))
            {
                return Json("[" + SubscriptionJson + "]");
            }

            return Json(FamiliesJson);
        });

        var service = CreateService(handler);

        var subscription = await service.SubscribeAsync("user-1", "demouser@microsoft.com", null, null, "eshop-pro");

        Assert.Equal(9001, subscription.SubscriptionId);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task SubscribeReconcilesByReferenceAfterTransportFailure()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/products.json")) return Json(ProductsJson);
            if (path.EndsWith("/customers/lookup.json")) return Json(CustomerJson);
            if (request.Method == HttpMethod.Get && Regex.IsMatch(path, "/customers/\\d+/subscriptions\\.json$")) return Json("[]");
            if (request.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json"))
            {
                throw new HttpRequestException("connection reset");
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/subscriptions/lookup.json"))
            {
                // The write did reach Maxio before the connection dropped.
                return Json(SubscriptionJson);
            }

            return Json(FamiliesJson);
        });

        var service = CreateService(handler);

        var subscription = await service.SubscribeAsync("user-1", "demouser@microsoft.com", null, null, "eshop-pro");

        Assert.Equal(9001, subscription.SubscriptionId);
    }

    [Fact]
    public async Task ListMySubscriptionsReturnsEmptyWhenCustomerNotFound()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = JsonContent("{}") });
        var service = CreateService(handler);

        var subscriptions = await service.ListMySubscriptionsAsync("unknown-user");

        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task SubscribeUnknownPlanThrowsNotFound()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/products.json")) return Json(ProductsJson);
            return Json(FamiliesJson);
        });

        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<BillingServiceException>(
            () => service.SubscribeAsync("user-1", "demouser@microsoft.com", null, null, "no-such-plan"));

        Assert.Equal((int)HttpStatusCode.NotFound, ex.StatusCode);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = JsonContent(body) };

    private static StringContent JsonContent(string body) =>
        new(body, System.Text.Encoding.UTF8, "application/json");

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public List<HttpRequestMessage> Requests { get; } = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responder(request));
        }
    }
}
