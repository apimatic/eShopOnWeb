using System.Net;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioSubscriptionBillingServiceTests
{
    private static readonly ShopperProfile Shopper =
        new("user-guid-1", "demouser@microsoft.com", "demouser@microsoft.com");

    private const string FamilyHandle = "eshop-subscribe";
    private const string ProHandle = "eshop-pro";

    private const string ProductJson = """
        {
          "product": {
            "id": 7126957,
            "handle": "eshop-pro",
            "name": "Pro Plan",
            "description": "Pro monthly",
            "price_in_cents": 29900,
            "interval": 1,
            "interval_unit": "month",
            "require_credit_card": false,
            "product_family": { "handle": "eshop-subscribe", "name": "eShop Subscribe" }
          }
        }
        """;

    private const string CustomerJson = """
        {
          "customer": {
            "id": 42,
            "reference": "demouser@microsoft.com",
            "email": "demouser@microsoft.com",
            "first_name": "demouser",
            "last_name": "Customer"
          }
        }
        """;

    private const string SubscriptionJson = """
        {
          "subscription": {
            "id": 99,
            "state": "active",
            "product_price_in_cents": 29900,
            "next_assessment_at": "2026-09-19T00:00:00Z",
            "current_period_ends_at": "2026-09-19T00:00:00Z",
            "reference": "demouser@microsoft.com:eshop-pro",
            "product": {
              "handle": "eshop-pro",
              "name": "Pro Plan",
              "price_in_cents": 29900,
              "interval": 1,
              "interval_unit": "month"
            }
          }
        }
        """;

    [Fact]
    public async Task ListPlans_MapsCentsAndFamilyHandle()
    {
        var handler = new StubHandler(request =>
        {
            Assert.Contains("/product_families/", request.RequestUri!.AbsolutePath);
            Assert.Contains("eshop-subscribe", Uri.UnescapeDataString(request.RequestUri!.AbsolutePath));
            return StubHandler.Json(HttpStatusCode.OK, $"[{ProductJson}]");
        });
        var sut = CreateSut(handler);

        var plans = await sut.ListPlansAsync(CancellationToken.None);

        var plan = Assert.Single(plans);
        Assert.Equal(ProHandle, plan.Handle);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.Equal(299.00m, plan.Price);
        Assert.Equal("month", plan.IntervalUnit);
        Assert.Equal(FamilyHandle, plan.ProductFamilyHandle);
        Assert.False(plan.RequireCreditCard);
    }

    [Fact]
    public async Task ListMySubscriptions_WhenNoCustomer_ReturnsEmpty()
    {
        var handler = new StubHandler(request =>
        {
            Assert.Contains("/customers/lookup", request.RequestUri!.AbsolutePath);
            return StubHandler.Empty(HttpStatusCode.NotFound);
        });
        var sut = CreateSut(handler);

        var result = await sut.ListMySubscriptionsAsync(Shopper, CancellationToken.None);

        Assert.Empty(result);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task Subscribe_WhenSubscriptionExists_DoesNotCreate()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/products/handle/"))
            {
                return StubHandler.Json(HttpStatusCode.OK, ProductJson);
            }

            if (path.Contains("/customers/lookup"))
            {
                return StubHandler.Json(HttpStatusCode.OK, CustomerJson);
            }

            if (path.Contains("/subscriptions/lookup"))
            {
                return StubHandler.Json(HttpStatusCode.OK, SubscriptionJson);
            }

            return StubHandler.Empty(HttpStatusCode.InternalServerError);
        });
        var sut = CreateSut(handler);

        var result = await sut.SubscribeAsync(Shopper, ProHandle, CancellationToken.None);

        Assert.False(result.Created);
        Assert.Equal(99, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal(299.00m, result.Subscription.Price);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task Subscribe_CreatesCustomerAndSubscription()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/products/handle/"))
            {
                return StubHandler.Json(HttpStatusCode.OK, ProductJson);
            }

            if (path.Contains("/customers/lookup"))
            {
                return StubHandler.Empty(HttpStatusCode.NotFound);
            }

            if (path.Contains("/customers") && request.Method == HttpMethod.Post)
            {
                return StubHandler.Json(HttpStatusCode.Created, CustomerJson);
            }

            if (path.Contains("/subscriptions/lookup"))
            {
                return StubHandler.Empty(HttpStatusCode.NotFound);
            }

            if (path.Contains("/subscriptions") && request.Method == HttpMethod.Post)
            {
                return StubHandler.Json(HttpStatusCode.Created, SubscriptionJson);
            }

            return StubHandler.Empty(HttpStatusCode.InternalServerError);
        });
        var sut = CreateSut(handler);

        var result = await sut.SubscribeAsync(Shopper, ProHandle, CancellationToken.None);

        Assert.True(result.Created);
        Assert.Equal(99, result.Subscription.Id);
        Assert.Equal(ProHandle, result.Subscription.ProductHandle);
        Assert.Equal(2, handler.Requests.Count(r => r.Method == HttpMethod.Post));
    }

    [Fact]
    public async Task Subscribe_UnknownPlan_ReturnsNotFound()
    {
        var handler = new StubHandler(_ => StubHandler.Empty(HttpStatusCode.NotFound));
        var sut = CreateSut(handler);

        var ex = await Assert.ThrowsAsync<MaxioBillingException>(
            () => sut.SubscribeAsync(Shopper, "no-such-plan", CancellationToken.None));

        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task ListPlans_WhenUnconfigured_ThrowsServiceUnavailable()
    {
        var handler = new StubHandler(_ => StubHandler.Empty(HttpStatusCode.OK));
        var sut = CreateSut(handler, configured: false);

        var ex = await Assert.ThrowsAsync<MaxioBillingException>(() => sut.ListPlansAsync(CancellationToken.None));
        Assert.Equal(503, ex.StatusCode);
    }

    private static MaxioSubscriptionBillingService CreateSut(StubHandler handler, bool configured = true)
    {
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), new MaxioAdvancedBillingClientOptions
        {
            Retry = RetryOptions.Default(),
            BasicAuth = new BasicAuthCredentials { Username = "test-key", Password = "x" }
        });
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = configured ? "test-key" : "",
            Subdomain = configured ? "example" : "",
            ProductFamilyHandle = configured ? FamilyHandle : ""
        });
        var logger = Substitute.For<IAppLogger<MaxioSubscriptionBillingService>>();
        return new MaxioSubscriptionBillingService(client, options, logger);
    }
}
