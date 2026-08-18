using System.Net;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioSubscriptionBillingServiceTests
{
    private const string FamilyHandle = "eshop-subscribe";
    private const string ProHandle = "eshop-pro";
    private const string UserId = "user-guid-1";

    [Fact]
    public async Task ListPlansAsync_ReturnsPlansFromConfiguredFamily()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/product_families.json", StringComparison.Ordinal))
            {
                return StubHandler.Json(HttpStatusCode.OK, FamilyListJson);
            }

            if (path.Contains("/product_families/10/products.json", StringComparison.Ordinal))
            {
                return StubHandler.Json(HttpStatusCode.OK, ProductListJson);
            }

            return StubHandler.Json(HttpStatusCode.NotFound, "{}");
        });

        var service = CreateService(handler);

        var plans = await service.ListPlansAsync(CancellationToken.None);

        Assert.Equal(2, plans.Count);
        Assert.Equal(ProHandle, plans[0].Handle);
        Assert.Equal(29900, plans[0].PriceInCents);
        Assert.Equal("month", plans[0].IntervalUnit);
        Assert.Equal("basic-plan", plans[1].Handle);
        Assert.Equal(2900, plans[1].PriceInCents);
    }

    [Fact]
    public async Task SubscribeAsync_ReturnsExistingLiveSubscriptionWithoutCreatingAnother()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/customers/lookup.json", StringComparison.Ordinal))
            {
                return StubHandler.Json(HttpStatusCode.OK, CustomerJson);
            }

            if (path.Contains("/customers/42/subscriptions.json", StringComparison.Ordinal))
            {
                return StubHandler.Json(HttpStatusCode.OK, LiveSubscriptionListJson);
            }

            if (path.EndsWith("/subscriptions.json", StringComparison.Ordinal) && request.Method == HttpMethod.Post)
            {
                return StubHandler.Json(HttpStatusCode.InternalServerError, "{}");
            }

            return StubHandler.Json(HttpStatusCode.NotFound, "{}");
        });

        var service = CreateService(handler);
        var result = await service.SubscribeAsync(Shopper(), ProHandle, CancellationToken.None);

        Assert.False(result.Created);
        Assert.Equal(ProHandle, result.Subscription.ProductHandle);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal(29900, result.Subscription.PriceInCents);
        Assert.NotNull(result.Subscription.NextBillingDate);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerAndSubscriptionWhenMissing()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/customers/lookup.json", StringComparison.Ordinal))
            {
                return StubHandler.Json(HttpStatusCode.NotFound, "{}");
            }

            if (path.EndsWith("/customers.json", StringComparison.Ordinal) && request.Method == HttpMethod.Post)
            {
                return StubHandler.Json(HttpStatusCode.Created, CustomerJson);
            }

            if (path.Contains("/customers/42/subscriptions.json", StringComparison.Ordinal))
            {
                return StubHandler.Json(HttpStatusCode.OK, "[]");
            }

            if (path.Contains("/products/handle/", StringComparison.Ordinal))
            {
                return StubHandler.Json(HttpStatusCode.OK, ProductJson);
            }

            if (path.EndsWith("/subscriptions.json", StringComparison.Ordinal) && request.Method == HttpMethod.Post)
            {
                return StubHandler.Json(HttpStatusCode.Created, CreatedSubscriptionJson);
            }

            return StubHandler.Json(HttpStatusCode.NotFound, "{}");
        });

        var service = CreateService(handler);
        var result = await service.SubscribeAsync(Shopper(), ProHandle, CancellationToken.None);

        Assert.True(result.Created);
        Assert.Equal(ProHandle, result.Subscription.ProductHandle);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal(2, handler.Requests.Count(r => r.Method == HttpMethod.Post));
    }

    [Fact]
    public async Task ListMySubscriptionsAsync_ReturnsEmptyWhenCustomerDoesNotExist()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.NotFound, "{}"));
        var service = CreateService(handler);

        var listed = await service.ListMySubscriptionsAsync(UserId, CancellationToken.None);

        Assert.Empty(listed);
    }

    private static MaxioSubscriptionBillingService CreateService(StubHandler handler)
    {
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.test")
        }, new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = "test-key", Password = "x" },
            Retry = RetryOptions.Default() with { MaxRetries = 1 }
        });

        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "example",
            ProductFamilyHandle = FamilyHandle
        });

        return new MaxioSubscriptionBillingService(client, options, NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    private static ShopperIdentity Shopper() =>
        new(UserId, "demouser@microsoft.com", "demouser", "Customer");

    private const string FamilyListJson = """
        [{ "product_family": { "id": 10, "handle": "eshop-subscribe", "name": "eShop Subscribe" } }]
        """;

    private const string ProductListJson = """
        [
          { "product": { "handle": "eshop-pro", "name": "Pro", "price_in_cents": 29900, "interval": 1, "interval_unit": "month", "require_credit_card": false } },
          { "product": { "handle": "basic-plan", "name": "Basic", "price_in_cents": 2900, "interval": 1, "interval_unit": "month", "require_credit_card": false } }
        ]
        """;

    private const string CustomerJson = """
        { "customer": { "id": 42, "reference": "user-guid-1", "email": "demouser@microsoft.com", "first_name": "demouser", "last_name": "Customer" } }
        """;

    private const string ProductJson = """
        { "product": { "handle": "eshop-pro", "name": "Pro", "price_in_cents": 29900, "interval": 1, "interval_unit": "month", "require_credit_card": false } }
        """;

    private const string LiveSubscriptionListJson = """
        [{ "subscription": {
            "id": 99,
            "state": "active",
            "product_price_in_cents": 29900,
            "next_assessment_at": "2026-09-19T00:00:00Z",
            "product": { "handle": "eshop-pro", "name": "Pro", "price_in_cents": 29900, "interval": 1, "interval_unit": "month" }
        } }]
        """;

    private const string CreatedSubscriptionJson = """
        { "subscription": {
            "id": 100,
            "state": "active",
            "product_price_in_cents": 29900,
            "next_assessment_at": "2026-09-19T00:00:00Z",
            "product": { "handle": "eshop-pro", "name": "Pro", "price_in_cents": 29900, "interval": 1, "interval_unit": "month" }
        } }
        """;
}
