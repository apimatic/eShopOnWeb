using System.Net;
using System.Text;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.PublicApi.Maxio;

public class SubscriptionBillingServiceTests
{
    private const string FamilyHandle = "eshop-subscribe";
    private const string UserRef = "demouser@microsoft.com";

    [Fact]
    public async Task ListPlans_ReturnsMappedPlans()
    {
        var handler = new StubHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.Contains("products"))
            {
                return Json(HttpStatusCode.OK,
                    "[{\"product\":{\"id\":7,\"name\":\"Pro Plan\",\"handle\":\"eshop-pro\",\"price_in_cents\":29900,\"interval\":1,\"interval_unit\":\"month\"}}," +
                    "{\"product\":{\"id\":8,\"name\":\"Old Plan\",\"handle\":\"old-plan\",\"price_in_cents\":100,\"interval\":1,\"interval_unit\":\"month\",\"archived_at\":\"2026-01-01T00:00:00Z\"}}]");
            }
            if (path.Contains("product_families"))
            {
                return Json(HttpStatusCode.OK,
                    "[{\"product_family\":{\"id\":42,\"name\":\"Fam\",\"handle\":\"eshop-subscribe\"}}]");
            }
            throw new InvalidOperationException("Unexpected request: " + path);
        });

        var plans = await CreateService(handler).ListPlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal("Pro Plan", plan.Name);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.Equal(1, plan.Interval);
        Assert.Equal("month", plan.IntervalUnit);
    }

    [Fact]
    public async Task Subscribe_CreatesCustomerAndSubscription_WhenNoneExist()
    {
        var handler = new StubHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (req.Method == HttpMethod.Get && path.Contains("customers") && !path.Contains("subscriptions"))
            {
                return Json(HttpStatusCode.NotFound, "{}");
            }
            if (req.Method == HttpMethod.Post && path.Contains("customers"))
            {
                return Json(HttpStatusCode.Created,
                    "{\"customer\":{\"id\":123,\"reference\":\"demouser@microsoft.com\",\"first_name\":\"demouser\",\"last_name\":\"eShopOnWeb\",\"email\":\"demouser@microsoft.com\"}}");
            }
            if (req.Method == HttpMethod.Get && path.Contains("subscriptions"))
            {
                return Json(HttpStatusCode.OK, "[]");
            }
            if (req.Method == HttpMethod.Post && path.Contains("subscriptions"))
            {
                return Json(HttpStatusCode.Created,
                    "{\"subscription\":{\"id\":900,\"state\":\"active\",\"reference\":\"demouser@microsoft.com:eshop-pro\",\"product_price_in_cents\":29900,\"next_assessment_at\":\"2026-09-25T00:00:00Z\",\"product\":{\"id\":7,\"name\":\"Pro Plan\",\"handle\":\"eshop-pro\"}}}");
            }
            if (path.Contains("products"))
            {
                return Json(HttpStatusCode.OK,
                    "[{\"product\":{\"id\":7,\"name\":\"Pro Plan\",\"handle\":\"eshop-pro\",\"price_in_cents\":29900,\"interval\":1,\"interval_unit\":\"month\"}}]");
            }
            if (path.Contains("product_families"))
            {
                return Json(HttpStatusCode.OK,
                    "[{\"product_family\":{\"id\":42,\"name\":\"Fam\",\"handle\":\"eshop-subscribe\"}}]");
            }
            throw new InvalidOperationException("Unexpected request: " + req.Method + " " + path);
        });

        var result = await CreateService(handler).SubscribeAsync(UserRef, UserRef, "eshop-pro");

        Assert.False(result.AlreadyExisted);
        Assert.Equal(900, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal("Pro Plan", result.Subscription.ProductName);
        Assert.Equal(29900, result.Subscription.PriceInCents);
        Assert.NotNull(result.Subscription.NextBillingDate);

        var sentJson = handler.Captured.Last(r =>
            r.Method == "POST" && r.Path.Contains("subscriptions")).Body!;
        Assert.Contains("\"product_handle\":\"eshop-pro\"", sentJson);
        Assert.Contains("\"customer_id\":123", sentJson);
        Assert.Contains("\"reference\":\"demouser@microsoft.com:eshop-pro\"", sentJson);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", sentJson);
    }

    [Fact]
    public async Task Subscribe_ReturnsExistingLiveSubscription_WithoutCreatingDuplicate()
    {
        var handler = new StubHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (req.Method == HttpMethod.Get && path.Contains("customers") && !path.Contains("subscriptions"))
            {
                return Json(HttpStatusCode.OK,
                    "{\"customer\":{\"id\":123,\"reference\":\"demouser@microsoft.com\"}}");
            }
            if (req.Method == HttpMethod.Get && path.Contains("subscriptions"))
            {
                return Json(HttpStatusCode.OK,
                    "[{\"subscription\":{\"id\":900,\"state\":\"active\",\"product_price_in_cents\":29900,\"next_assessment_at\":\"2026-09-25T00:00:00Z\",\"product\":{\"id\":7,\"name\":\"Pro Plan\",\"handle\":\"eshop-pro\"}}}]");
            }
            if (path.Contains("products"))
            {
                return Json(HttpStatusCode.OK,
                    "[{\"product\":{\"id\":7,\"name\":\"Pro Plan\",\"handle\":\"eshop-pro\",\"price_in_cents\":29900,\"interval\":1,\"interval_unit\":\"month\"}}]");
            }
            if (path.Contains("product_families"))
            {
                return Json(HttpStatusCode.OK,
                    "[{\"product_family\":{\"id\":42,\"name\":\"Fam\",\"handle\":\"eshop-subscribe\"}}]");
            }
            throw new InvalidOperationException("Unexpected request: " + req.Method + " " + path);
        });

        var result = await CreateService(handler).SubscribeAsync(UserRef, UserRef, "eshop-pro");

        Assert.True(result.AlreadyExisted);
        Assert.Equal(900, result.Subscription.Id);
        Assert.DoesNotContain(handler.Captured, r =>
            r.Method == "POST" && r.Path.Contains("subscriptions"));
        Assert.DoesNotContain(handler.Captured, r =>
            r.Method == "POST" && r.Path.Contains("customers"));
    }

    [Fact]
    public async Task Subscribe_UnknownPlan_ThrowsNotFound()
    {
        var handler = new StubHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.Contains("products"))
            {
                return Json(HttpStatusCode.OK,
                    "[{\"product\":{\"id\":7,\"name\":\"Pro Plan\",\"handle\":\"eshop-pro\",\"price_in_cents\":29900,\"interval\":1,\"interval_unit\":\"month\"}}]");
            }
            if (path.Contains("product_families"))
            {
                return Json(HttpStatusCode.OK,
                    "[{\"product_family\":{\"id\":42,\"name\":\"Fam\",\"handle\":\"eshop-subscribe\"}}]");
            }
            throw new InvalidOperationException("Unexpected request: " + path);
        });

        var ex = await Assert.ThrowsAsync<BillingException>(
            () => CreateService(handler).SubscribeAsync(UserRef, UserRef, "no-such-plan"));

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task Subscribe_Provider422_SurfacesProviderMessageAsClientError()
    {
        var handler = new StubHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (req.Method == HttpMethod.Get && path.Contains("customers") && !path.Contains("subscriptions"))
            {
                return Json(HttpStatusCode.OK, "{\"customer\":{\"id\":123}}");
            }
            if (req.Method == HttpMethod.Get && path.Contains("subscriptions"))
            {
                return Json(HttpStatusCode.OK, "[]");
            }
            if (req.Method == HttpMethod.Post && path.Contains("subscriptions"))
            {
                return Json(HttpStatusCode.UnprocessableEntity,
                    "{\"errors\":[\"No payment method was on file for the $299.00 balance\"]}");
            }
            if (path.Contains("products"))
            {
                return Json(HttpStatusCode.OK,
                    "[{\"product\":{\"id\":7,\"name\":\"Pro Plan\",\"handle\":\"eshop-pro\",\"price_in_cents\":29900,\"interval\":1,\"interval_unit\":\"month\"}}]");
            }
            if (path.Contains("product_families"))
            {
                return Json(HttpStatusCode.OK,
                    "[{\"product_family\":{\"id\":42,\"name\":\"Fam\",\"handle\":\"eshop-subscribe\"}}]");
            }
            throw new InvalidOperationException("Unexpected request: " + req.Method + " " + path);
        });

        var ex = await Assert.ThrowsAsync<BillingException>(
            () => CreateService(handler).SubscribeAsync(UserRef, UserRef, "eshop-pro"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, ex.StatusCode);
        Assert.Contains("No payment method", ex.Message);
    }

    [Fact]
    public async Task ListMySubscriptions_ReturnsEmpty_WhenCustomerNotFound()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.NotFound, "{}"));

        var subscriptions = await CreateService(handler).ListMySubscriptionsAsync(UserRef);

        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task TransportFailure_SurfacesAsServiceUnavailable()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection reset"));

        var ex = await Assert.ThrowsAsync<BillingException>(
            () => CreateService(handler).ListMySubscriptionsAsync(UserRef));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
    }

    private static SubscriptionBillingService CreateService(StubHandler handler)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials { Username = "test-key", Password = "x" },
            // Keep transport-retry tests fast: 1 retry instead of the default 3.
            Retry = RetryOptions.Default() with { MaxRetries = 1 }
        };
        options.Server.Production.Us.Site = "test";

        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), options);
        var settings = Options.Create(new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "test",
            ProductFamilyHandle = FamilyHandle
        });

        return new SubscriptionBillingService(
            client,
            settings,
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<SubscriptionBillingService>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        // Bodies are captured at send time — the SDK disposes request content afterwards.
        public List<(string Method, string Path, string? Body)> Captured { get; } = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Captured.Add((request.Method.Method, request.RequestUri!.AbsolutePath, body));
            return _responder(request);
        }
    }
}
