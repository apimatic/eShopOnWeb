using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// Unit tests for the Maxio integration service, exercised over the SDK's HttpClient
/// seam with a stub handler — no network, no SDK internals.
/// </summary>
public class MaxioBillingServiceTests
{
    private const string CustomerReference = "eshopweb-user-u-1";
    private const string SubscriptionReference = "eshopweb-user-u-1-sub-eshop-pro";

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerAndSubscription()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.Contains("/customers/lookup"))
                return StubHandler.NotFound();

            if (request.Method == HttpMethod.Post && path.EndsWith("/customers.json"))
                return StubHandler.Json(HttpStatusCode.Created, CustomerJson(42));

            if (request.Method == HttpMethod.Get && path.Contains("/subscriptions/lookup"))
                return StubHandler.NotFound();

            if (request.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json"))
                return StubHandler.Json(HttpStatusCode.Created, SubscriptionJson(7, "active"));

            return StubHandler.NotFound();
        });

        var service = CreateService(handler);
        var result = await service.SubscribeAsync(Subscriber(), "eshop-pro", default);

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(7, result.SubscriptionId);
        Assert.Equal("eshop-pro", result.PlanHandle);
        Assert.Equal("Pro Plan", result.PlanName);
        Assert.Equal(299.00m, result.Price);
        Assert.Equal("active", result.State);
        Assert.Equal(CustomerReference, result.CustomerReference);
        Assert.NotNull(result.NextBillingDate);

        var customerPost = Assert.Single(handler.Requests, r => r.Method == HttpMethod.Post && r.Path.EndsWith("/customers.json"));
        Assert.Contains($"\"reference\":\"{CustomerReference}\"", customerPost.Body);
        Assert.Contains("\"email\":\"demouser@microsoft.com\"", customerPost.Body);

        var subscriptionPost = Assert.Single(handler.Requests, r => r.Method == HttpMethod.Post && r.Path.EndsWith("/subscriptions.json"));
        Assert.Contains("\"product_handle\":\"eshop-pro\"", subscriptionPost.Body);
        Assert.Contains("\"customer_id\":42", subscriptionPost.Body);
        Assert.Contains($"\"reference\":\"{SubscriptionReference}\"", subscriptionPost.Body);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", subscriptionPost.Body);
    }

    [Fact]
    public async Task SubscribeAsync_WhenCustomerAlreadyExists_DoesNotCreateAnotherCustomer()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.Contains("/customers/lookup"))
                return StubHandler.Json(HttpStatusCode.OK, CustomerJson(42));

            if (request.Method == HttpMethod.Get && path.Contains("/subscriptions/lookup"))
                return StubHandler.NotFound();

            if (request.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json"))
                return StubHandler.Json(HttpStatusCode.Created, SubscriptionJson(7, "active"));

            return StubHandler.NotFound();
        });

        var service = CreateService(handler);
        var result = await service.SubscribeAsync(Subscriber(), "eshop-pro", default);

        Assert.Equal(7, result.SubscriptionId);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post && r.Path.EndsWith("/customers.json"));
    }

    [Fact]
    public async Task SubscribeAsync_WhenSubscriptionAlreadyExists_ReturnsItIdempotently()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.Contains("/customers/lookup"))
                return StubHandler.Json(HttpStatusCode.OK, CustomerJson(42));

            if (request.Method == HttpMethod.Get && path.Contains("/subscriptions/lookup"))
                return StubHandler.Json(HttpStatusCode.OK, SubscriptionJson(7, "active"));

            return StubHandler.NotFound();
        });

        var service = CreateService(handler);
        var result = await service.SubscribeAsync(Subscriber(), "eshop-pro", default);

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(7, result.SubscriptionId);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task SubscribeAsync_CreateRaceUnprocessableEntity_ReFindsSubscriptionInsteadOfCreatingTwice()
    {
        var subscriptionLookups = 0;
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.Contains("/customers/lookup"))
                return StubHandler.Json(HttpStatusCode.OK, CustomerJson(42));

            if (request.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json"))
                return StubHandler.Json(HttpStatusCode.UnprocessableEntity, "{\"errors\":[\"Reference has already been taken\"]}");

            if (request.Method == HttpMethod.Get && path.Contains("/subscriptions/lookup"))
            {
                subscriptionLookups++;
                return subscriptionLookups == 1 ? StubHandler.NotFound() : StubHandler.Json(HttpStatusCode.OK, SubscriptionJson(7, "active"));
            }

            return StubHandler.NotFound();
        });

        var service = CreateService(handler);
        var result = await service.SubscribeAsync(Subscriber(), "eshop-pro", default);

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(1, handler.Requests.Count(r => r.Method == HttpMethod.Post && r.Path.EndsWith("/subscriptions.json")));
    }

    [Fact]
    public async Task SubscribeAsync_CustomerCreateRaceUnprocessableEntity_ReLooksUpCustomer()
    {
        var customerLookups = 0;
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.Contains("/customers/lookup"))
            {
                customerLookups++;
                return customerLookups == 1 ? StubHandler.NotFound() : StubHandler.Json(HttpStatusCode.OK, CustomerJson(42));
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/customers.json"))
                return StubHandler.Json(HttpStatusCode.UnprocessableEntity, "{\"errors\":[\"reference has already been taken\"]}");

            if (request.Method == HttpMethod.Get && path.Contains("/subscriptions/lookup"))
                return StubHandler.NotFound();

            if (request.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json"))
                return StubHandler.Json(HttpStatusCode.Created, SubscriptionJson(7, "active"));

            return StubHandler.NotFound();
        });

        var service = CreateService(handler);
        var result = await service.SubscribeAsync(Subscriber(), "eshop-pro", default);

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(1, handler.Requests.Count(r => r.Method == HttpMethod.Post && r.Path.EndsWith("/customers.json")));
    }

    [Fact]
    public async Task SubscribeAsync_GenuineValidationRejection_SurfacesValidationFailureWithDetails()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.Contains("/customers/lookup"))
                return StubHandler.Json(HttpStatusCode.OK, CustomerJson(42));

            if (request.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json"))
                return StubHandler.Json(HttpStatusCode.UnprocessableEntity, "{\"errors\":[\"Product is unavailable\"]}");

            if (request.Method == HttpMethod.Get && path.Contains("/subscriptions/lookup"))
                return StubHandler.NotFound();

            return StubHandler.NotFound();
        });

        var service = CreateService(handler);

        var exception = await Assert.ThrowsAsync<MaxioBillingException>(
            () => service.SubscribeAsync(Subscriber(), "eshop-pro", default));

        Assert.Equal(MaxioBillingFailureKind.Validation, exception.Kind);
        Assert.Contains("Product is unavailable", exception.Details);
    }

    [Fact]
    public async Task ListPlansAsync_ListsProductsOfConfiguredFamily()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("product_families/") && path.EndsWith("/products.json"))
            {
                Assert.Contains("/1/", path);
                return StubHandler.Json(HttpStatusCode.OK,
                    "[" + ProductJson(2, "eshop-pro", "Pro Plan", 29900) + "," + ProductJson(3, "basic-plan", "Basic Plan", 2900) + "]");
            }

            if (path.EndsWith("/product_families.json"))
                return StubHandler.Json(HttpStatusCode.OK, "[" + ProductFamilyJson(1) + "]");

            return StubHandler.NotFound();
        });

        var service = CreateService(handler);
        var plans = await service.ListPlansAsync(default);

        Assert.Equal(2, plans.Count);
        var pro = plans.Single(p => p.Handle == "eshop-pro");
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(299.00m, pro.Price);
        Assert.Equal(1, pro.Interval);
        Assert.Equal("month", pro.IntervalUnit);
    }

    [Fact]
    public async Task ListMySubscriptionsAsync_WhenCustomerNeverProvisioned_ReturnsEmptyWithoutProvisioning()
    {
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("/customers/lookup"))
                return StubHandler.NotFound();
            return StubHandler.NotFound();
        });

        var service = CreateService(handler);
        var subscriptions = await service.ListMySubscriptionsAsync(CustomerReference, default);

        Assert.Empty(subscriptions);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task ListMySubscriptionsAsync_MapsRecordedSubscriptions()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/customers/lookup"))
                return StubHandler.Json(HttpStatusCode.OK, CustomerJson(42));

            if (path.EndsWith("/subscriptions.json"))
                return StubHandler.Json(HttpStatusCode.OK, "[" + SubscriptionJson(7, "active") + "]");

            return StubHandler.NotFound();
        });

        var service = CreateService(handler);
        var subscriptions = await service.ListMySubscriptionsAsync(CustomerReference, default);

        var subscription = Assert.Single(subscriptions);
        Assert.Equal(7, subscription.SubscriptionId);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal("active", subscription.State);
    }

    [Fact]
    public void Constructor_WhenConfigurationMissing_FailsClosedAsNotConfigured()
    {
        var handler = new StubHandler(_ => StubHandler.NotFound());
        var settings = new MaxioSettings { ApiKey = "", Subdomain = "site", ProductFamilyHandle = "family" };

        var exception = Assert.Throws<MaxioBillingException>(() => CreateService(handler, settings));

        Assert.Equal(MaxioBillingFailureKind.NotConfigured, exception.Kind);
    }

    [Fact]
    public async Task TransportFailure_IsNormalizedToUnavailable()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection reset"));
        var service = CreateService(handler);

        var exception = await Assert.ThrowsAsync<MaxioBillingException>(
            () => service.SubscribeAsync(Subscriber(), "eshop-pro", default));

        Assert.Equal(MaxioBillingFailureKind.Unavailable, exception.Kind);
    }

    private static MaxioBillingService CreateService(StubHandler handler, MaxioSettings? settings = null)
    {
        var options = new MaxioAdvancedBillingClientOptions();
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), options);
        return new MaxioBillingService(
            client,
            settings ?? new MaxioSettings { ApiKey = "test-key", Subdomain = "test-site", ProductFamilyHandle = "eshop-subscribe" },
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<MaxioBillingService>.Instance);
    }

    private static MaxioSubscriber Subscriber() =>
        new(CustomerReference, "demouser@microsoft.com", "Demo", "User");

    private static string Normalize(string json) => json.Replace(" ", "").Replace("\r", "").Replace("\n", "");

    private static string CustomerJson(int id) =>
        "{\"customer\":{\"id\":" + id + ",\"reference\":\"" + CustomerReference + "\",\"email\":\"demouser@microsoft.com\",\"first_name\":\"Demo\",\"last_name\":\"User\"}}";

    private static string ProductFamilyJson(int id) =>
        "{\"product_family\":{\"id\":" + id + ",\"handle\":\"eshop-subscribe\",\"name\":\"eShop Subscribe\"}}";

    private static string ProductJson(int id, string handle, string name, long priceInCents) =>
        "{\"product\":{\"id\":" + id + ",\"handle\":\"" + handle + "\",\"name\":\"" + name + "\",\"price_in_cents\":" + priceInCents +
        ",\"interval\":1,\"interval_unit\":\"month\",\"product_family\":{\"id\":1,\"handle\":\"eshop-subscribe\"}}}";

    private static string SubscriptionJson(int id, string state) =>
        "{\"subscription\":{\"id\":" + id + ",\"state\":\"" + state + "\",\"product_price_in_cents\":29900," +
        "\"current_period_ends_at\":\"2026-11-06T00:00:00Z\",\"next_assessment_at\":\"2026-11-06T00:00:00Z\"," +
        "\"product\":{\"id\":2,\"handle\":\"eshop-pro\",\"name\":\"Pro Plan\",\"price_in_cents\":29900}," +
        "\"customer\":{\"id\":42,\"reference\":\"" + CustomerReference + "\"}}}";

    /// <summary>
    /// Routes stubbed Maxio responses by request method and path; records a snapshot of
    /// every request (method, path, body) so tests can assert the exact outbound call
    /// sequence — the HttpClient disposes request objects after send, so snapshots must
    /// be taken in-flight.
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        public sealed record CapturedRequest(HttpMethod Method, string Path, string Body);

        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public List<CapturedRequest> Requests { get; } = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        public static HttpResponseMessage NotFound() => new(HttpStatusCode.NotFound);

        public static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
        {
            var body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;
            Requests.Add(new CapturedRequest(request.Method, request.RequestUri!.AbsolutePath, Normalize(body)));
            return Task.FromResult(_responder(request));
        }
    }
}