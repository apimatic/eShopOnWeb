using System.Net;
using System.Net.Http;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioSubscriptionBillingServiceTests
{
    private const string Family = "eshop-subscribe";
    private const string ProHandle = "eshop-pro";

    [Fact]
    public async Task ListPlansAsync_FiltersByProductFamilyAndMapsPrice()
    {
        var handler = new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Contains("/products", request.RequestUri!.AbsolutePath, StringComparison.OrdinalIgnoreCase);
            return StubHandler.Json(HttpStatusCode.OK, """
                [
                  {
                    "product": {
                      "id": 1,
                      "name": "Pro",
                      "handle": "eshop-pro",
                      "description": "Pro plan",
                      "price_in_cents": 29900,
                      "interval": 1,
                      "interval_unit": "month",
                      "require_credit_card": false,
                      "product_family": { "handle": "eshop-subscribe" }
                    }
                  },
                  {
                    "product": {
                      "id": 2,
                      "name": "Other family",
                      "handle": "other",
                      "price_in_cents": 100,
                      "interval": 1,
                      "interval_unit": "month",
                      "product_family": { "handle": "somewhere-else" }
                    }
                  }
                ]
                """);
        });

        var service = CreateService(handler);

        var plans = await service.ListPlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal(ProHandle, plan.Handle);
        Assert.Equal("Pro", plan.Name);
        Assert.Equal(299.00m, plan.Price);
        Assert.Equal("month", plan.IntervalUnit);
        Assert.False(plan.RequireCreditCard);
    }

    [Fact]
    public async Task SubscribeAsync_ReturnsExistingSubscriptionWithoutCreatingAnother()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/products/handle/", StringComparison.OrdinalIgnoreCase))
            {
                return ProductResponse();
            }

            if (path.Contains("/customers/lookup", StringComparison.OrdinalIgnoreCase)
                || (request.Method == HttpMethod.Get && path.Contains("/customers/", StringComparison.OrdinalIgnoreCase) && path.Contains("lookup", StringComparison.OrdinalIgnoreCase)))
            {
                return CustomerResponse();
            }

            if (request.Method == HttpMethod.Get && path.Contains("/subscriptions", StringComparison.OrdinalIgnoreCase))
            {
                return SubscriptionResponse();
            }

            return StubHandler.Empty(HttpStatusCode.NotFound);
        });

        var service = CreateService(handler);

        var result = await service.SubscribeAsync(NewSubscribeRequest());

        Assert.True(result.AlreadyExisted);
        Assert.Equal(99, result.Id);
        Assert.Equal("active", result.State);
        Assert.Equal(299.00m, result.Price);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerAndSubscriptionWhenMissing()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            var query = request.RequestUri!.Query;

            if (path.Contains("/products/handle/", StringComparison.OrdinalIgnoreCase))
            {
                return ProductResponse();
            }

            if (request.Method == HttpMethod.Get && (path.Contains("/customers/lookup", StringComparison.OrdinalIgnoreCase) || query.Contains("reference=")))
            {
                return StubHandler.Empty(HttpStatusCode.NotFound);
            }

            if (request.Method == HttpMethod.Post && path.Contains("/customers", StringComparison.OrdinalIgnoreCase)
                && !path.Contains("/subscriptions", StringComparison.OrdinalIgnoreCase))
            {
                return CustomerResponse();
            }

            if (request.Method == HttpMethod.Get && path.Contains("/customers/", StringComparison.OrdinalIgnoreCase)
                && path.Contains("/subscriptions", StringComparison.OrdinalIgnoreCase))
            {
                return StubHandler.Json(HttpStatusCode.OK, "[]");
            }

            if (request.Method == HttpMethod.Get && path.Contains("/subscriptions", StringComparison.OrdinalIgnoreCase))
            {
                return StubHandler.Empty(HttpStatusCode.NotFound);
            }

            if (request.Method == HttpMethod.Post && path.Contains("/subscriptions", StringComparison.OrdinalIgnoreCase))
            {
                return SubscriptionResponse(alreadyActive: true);
            }

            return StubHandler.Empty(HttpStatusCode.NotFound);
        });

        var service = CreateService(handler);

        var result = await service.SubscribeAsync(NewSubscribeRequest());

        Assert.False(result.AlreadyExisted);
        Assert.Equal(99, result.Id);
        Assert.Equal(ProHandle, result.ProductHandle);
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.Contains("customers", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.Contains("subscriptions", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SubscribeAsync_UnknownPlan_ThrowsCallerSafe400()
    {
        var handler = new StubHandler(_ => StubHandler.Empty(HttpStatusCode.NotFound));
        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<BillingException>(() => service.SubscribeAsync(NewSubscribeRequest("no-such-plan")));

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("no-such-plan", ex.Message);
        Assert.DoesNotContain("SdkException", ex.Message);
    }

    [Fact]
    public async Task ListMySubscriptionsAsync_ReturnsEmptyWhenCustomerMissing()
    {
        var handler = new StubHandler(_ => StubHandler.Empty(HttpStatusCode.NotFound));
        var service = CreateService(handler);

        var result = await service.ListMySubscriptionsAsync("user-1");

        Assert.Empty(result);
    }

    [Fact]
    public async Task ListMySubscriptionsAsync_MapsOpenSubscriptions()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.Contains("/customers/lookup", StringComparison.OrdinalIgnoreCase)
                || request.RequestUri!.Query.Contains("reference="))
            {
                return CustomerResponse();
            }

            if (path.Contains("/subscriptions", StringComparison.OrdinalIgnoreCase))
            {
                return StubHandler.Json(HttpStatusCode.OK, """
                    [
                      {
                        "subscription": {
                          "id": 99,
                          "state": "active",
                          "product_price_in_cents": 29900,
                          "current_period_ends_at": "2026-09-19T00:00:00Z",
                          "next_assessment_at": "2026-09-19T00:00:00Z",
                          "reference": "user-1:eshop-pro",
                          "product": { "handle": "eshop-pro", "name": "Pro", "price_in_cents": 29900 }
                        }
                      }
                    ]
                    """);
            }

            return StubHandler.Empty(HttpStatusCode.NotFound);
        });

        var service = CreateService(handler);

        var result = await service.ListMySubscriptionsAsync("user-1");

        var sub = Assert.Single(result);
        Assert.Equal(99, sub.Id);
        Assert.Equal("active", sub.State);
        Assert.Equal(new DateTimeOffset(2026, 9, 19, 0, 0, 0, TimeSpan.Zero), sub.NextBillingDate);
    }

    private static SubscribeShopperRequest NewSubscribeRequest(string handle = ProHandle) => new()
    {
        ShopperUserId = "user-1",
        Email = "demouser@microsoft.com",
        FirstName = "Demo",
        LastName = "User",
        ProductHandle = handle
    };

    private static MaxioSubscriptionBillingService CreateService(StubHandler handler)
    {
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.chargify.com")
        }, new MaxioAdvancedBillingClientOptions());

        var options = Options.Create(new MaxioOptions
        {
            ProductFamilyHandle = Family,
            Subdomain = "example",
            ApiKey = "test"
        });

        return new MaxioSubscriptionBillingService(client, options, NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    private static HttpResponseMessage ProductResponse() => StubHandler.Json(HttpStatusCode.OK, """
        {
          "product": {
            "id": 1,
            "name": "Pro",
            "handle": "eshop-pro",
            "price_in_cents": 29900,
            "interval": 1,
            "interval_unit": "month",
            "require_credit_card": false,
            "product_family": { "handle": "eshop-subscribe" }
          }
        }
        """);

    private static HttpResponseMessage CustomerResponse() => StubHandler.Json(HttpStatusCode.OK, """
        {
          "customer": {
            "id": 10,
            "reference": "user-1",
            "email": "demouser@microsoft.com",
            "first_name": "Demo",
            "last_name": "User"
          }
        }
        """);

    private static HttpResponseMessage SubscriptionResponse(bool alreadyActive = true) => StubHandler.Json(HttpStatusCode.OK, """
        {
          "subscription": {
            "id": 99,
            "state": "active",
            "product_price_in_cents": 29900,
            "current_period_ends_at": "2026-09-19T00:00:00Z",
            "next_assessment_at": "2026-09-19T00:00:00Z",
            "reference": "user-1:eshop-pro",
            "product": { "handle": "eshop-pro", "name": "Pro", "price_in_cents": 29900 }
          }
        }
        """);
}
