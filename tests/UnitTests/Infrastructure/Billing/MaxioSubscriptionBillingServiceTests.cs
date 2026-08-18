using System.Net;
using System.Net.Http;
using System.Text;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioSubscriptionBillingServiceTests
{
    private const string FamilyHandle = "eshop-subscribe";
    private const string UserId = "user-guid-1";
    private const string ProductHandle = "eshop-pro";

    private static readonly ShopperIdentity Shopper = new(UserId, "demouser@microsoft.com", "Demouser", "Customer");

    [Fact]
    public async Task ListPlans_ReturnsFamilyProducts()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.Contains("product_families") && !path.Contains("products"))
            {
                return Json(HttpStatusCode.OK, """
                    [{"product_family":{"id":3023074,"handle":"eshop-subscribe","name":"eShop Subscribe"}}]
                    """);
            }

            if (request.Method == HttpMethod.Get && path.Contains("/product_families/3023074/products"))
            {
                return Json(HttpStatusCode.OK, """
                    [{"product":{"id":1,"name":"Pro Plan","handle":"eshop-pro","description":"Pro","price_in_cents":29900,"interval":1,"interval_unit":"month"}}]
                    """);
            }

            return Json(HttpStatusCode.NotFound, "{}");
        });

        var service = CreateService(handler);

        var plans = await service.ListPlansAsync(CancellationToken.None);

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.Equal("month", plan.IntervalUnit);
    }

    [Fact]
    public async Task ListMySubscriptions_ReturnsEmpty_WhenCustomerMissing()
    {
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("/customers/lookup"))
            {
                return Json(HttpStatusCode.NotFound, "{}");
            }

            return Json(HttpStatusCode.NotFound, "{}");
        });

        var service = CreateService(handler);

        var subscriptions = await service.ListMySubscriptionsAsync(UserId, CancellationToken.None);

        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task Subscribe_CreatesCustomerAndSubscription()
    {
        var createdCustomers = 0;
        var createdSubscriptions = 0;
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;

            if (request.Method == HttpMethod.Get && path.Contains("product_families") && !path.Contains("products"))
            {
                return Json(HttpStatusCode.OK, """
                    [{"product_family":{"id":3023074,"handle":"eshop-subscribe"}}]
                    """);
            }

            if (request.Method == HttpMethod.Get && path.Contains("/products"))
            {
                return Json(HttpStatusCode.OK, """
                    [{"product":{"id":1,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month"}}]
                    """);
            }

            if (request.Method == HttpMethod.Get && path.Contains("/customers/lookup"))
            {
                return Json(HttpStatusCode.NotFound, "{}");
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/customers.json") || (request.Method == HttpMethod.Post && path.Contains("/customers") && !path.Contains("subscriptions")))
            {
                createdCustomers++;
                return Json(HttpStatusCode.Created, """
                    {"customer":{"id":42,"reference":"user-guid-1","email":"demouser@microsoft.com","first_name":"Demouser","last_name":"Customer"}}
                    """);
            }

            if (request.Method == HttpMethod.Get && path.Contains("/subscriptions/lookup"))
            {
                return Json(HttpStatusCode.NotFound, "{}");
            }

            if (request.Method == HttpMethod.Get && path.Contains("/customers/42/subscriptions"))
            {
                return Json(HttpStatusCode.OK, "[]");
            }

            if (request.Method == HttpMethod.Post && path.Contains("/subscriptions"))
            {
                createdSubscriptions++;
                return Json(HttpStatusCode.Created, """
                    {"subscription":{"id":99,"reference":"user-guid-1:eshop-pro","state":"active","product_price_in_cents":29900,"next_assessment_at":"2026-09-19T00:00:00Z","currency":"USD","product":{"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900}}}
                    """);
            }

            return Json(HttpStatusCode.NotFound, "{}");
        });

        var service = CreateService(handler);

        var result = await service.SubscribeAsync(Shopper, ProductHandle, CancellationToken.None);

        Assert.True(result.Created);
        Assert.Equal(99, result.Subscription.Id);
        Assert.Equal("eshop-pro", result.Subscription.PlanHandle);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal(29900, result.Subscription.PriceInCents);
        Assert.Equal(1, createdCustomers);
        Assert.Equal(1, createdSubscriptions);
    }

    [Fact]
    public async Task Subscribe_IsIdempotent_WhenSubscriptionAlreadyExists()
    {
        var createdSubscriptions = 0;
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;

            if (request.Method == HttpMethod.Get && path.Contains("product_families") && !path.Contains("products"))
            {
                return Json(HttpStatusCode.OK, """
                    [{"product_family":{"id":3023074,"handle":"eshop-subscribe"}}]
                    """);
            }

            if (request.Method == HttpMethod.Get && path.Contains("/products"))
            {
                return Json(HttpStatusCode.OK, """
                    [{"product":{"id":1,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month"}}]
                    """);
            }

            if (request.Method == HttpMethod.Get && path.Contains("/customers/lookup"))
            {
                return Json(HttpStatusCode.OK, """
                    {"customer":{"id":42,"reference":"user-guid-1","email":"demouser@microsoft.com"}}
                    """);
            }

            if (request.Method == HttpMethod.Get && path.Contains("/subscriptions/lookup"))
            {
                return Json(HttpStatusCode.OK, """
                    {"subscription":{"id":99,"reference":"user-guid-1:eshop-pro","state":"active","product_price_in_cents":29900,"next_assessment_at":"2026-09-19T00:00:00Z","product":{"name":"Pro Plan","handle":"eshop-pro"}}}
                    """);
            }

            if (request.Method == HttpMethod.Post && path.Contains("/subscriptions"))
            {
                createdSubscriptions++;
                return Json(HttpStatusCode.Created, """{"subscription":{"id":100}}""");
            }

            return Json(HttpStatusCode.NotFound, "{}");
        });

        var service = CreateService(handler);

        var first = await service.SubscribeAsync(Shopper, ProductHandle, CancellationToken.None);
        var second = await service.SubscribeAsync(Shopper, ProductHandle, CancellationToken.None);

        Assert.False(first.Created);
        Assert.False(second.Created);
        Assert.Equal(99, first.Subscription.Id);
        Assert.Equal(99, second.Subscription.Id);
        Assert.Equal(0, createdSubscriptions);
    }

    [Fact]
    public async Task Subscribe_RejectsUnknownPlan()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.Contains("product_families") && !path.Contains("products"))
            {
                return Json(HttpStatusCode.OK, """
                    [{"product_family":{"id":3023074,"handle":"eshop-subscribe"}}]
                    """);
            }

            if (request.Method == HttpMethod.Get && path.Contains("/products"))
            {
                return Json(HttpStatusCode.OK, """
                    [{"product":{"id":1,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900}}]
                    """);
            }

            return Json(HttpStatusCode.NotFound, "{}");
        });

        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<BillingProviderException>(
            () => service.SubscribeAsync(Shopper, "not-a-plan", CancellationToken.None));

        Assert.Equal(400, ex.StatusCode);
    }

    private static MaxioSubscriptionBillingService CreateService(StubHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example.chargify.com") };
        var client = new MaxioAdvancedBillingClient(httpClient, MaxioBillingServiceCollectionExtensions.CreateClientOptions(new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "example",
            ProductFamilyHandle = FamilyHandle
        }));

        return new MaxioSubscriptionBillingService(
            client,
            Options.Create(new MaxioSettings
            {
                ApiKey = "test-key",
                Subdomain = "example",
                ProductFamilyHandle = FamilyHandle
            }),
            NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json)
        => new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }
}
