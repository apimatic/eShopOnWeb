using System.Net;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.PublicApi.Billing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioSubscriptionBillingServiceTests
{
    private static readonly ShopperIdentity DemoShopper = new(
        Reference: "demouser@microsoft.com",
        Email: "demouser@microsoft.com",
        FirstName: "Demouser",
        LastName: "eShopOnWeb");

    [Fact]
    public async Task ListPlans_ReturnsMappedPricesInDollars()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("products", StringComparison.OrdinalIgnoreCase)
                && path.Contains("product_families", StringComparison.OrdinalIgnoreCase))
            {
                return StubHandler.Json(HttpStatusCode.OK, """
                    [
                      {
                        "product": {
                          "handle": "eshop-pro",
                          "name": "Pro Plan",
                          "price_in_cents": 29900,
                          "interval": 1,
                          "interval_unit": "month"
                        }
                      },
                      {
                        "product": {
                          "handle": "basic-plan",
                          "name": "Basic Plan",
                          "price_in_cents": 2900,
                          "interval": 1,
                          "interval_unit": "month"
                        }
                      }
                    ]
                    """);
            }

            if (path.Contains("product_families", StringComparison.OrdinalIgnoreCase))
            {
                return StubHandler.Json(HttpStatusCode.OK, """
                    [
                      {
                        "product_family": {
                          "id": 3023074,
                          "handle": "eshop-subscribe",
                          "name": "eShop Subscribe"
                        }
                      }
                    ]
                    """);
            }

            return StubHandler.Json(HttpStatusCode.NotFound, "{}");
        });

        var service = CreateService(handler);

        var plans = await service.ListPlansAsync(CancellationToken.None);

        Assert.Equal(2, plans.Count);
        Assert.Contains(plans, p => p.Handle == "eshop-pro" && p.Price == 299.00m && p.IntervalUnit == "month");
        Assert.Contains(plans, p => p.Handle == "basic-plan" && p.Price == 29.00m);
    }

    [Fact]
    public async Task Subscribe_CreatesCustomerAndSubscription_WhenNoneExist()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            var method = request.Method;

            if (method == HttpMethod.Get && path.Contains("/products/handle/", StringComparison.OrdinalIgnoreCase))
            {
                return StubHandler.Json(HttpStatusCode.OK, """
                    { "product": { "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900, "interval": 1, "interval_unit": "month" } }
                    """);
            }

            if (method == HttpMethod.Get && path.Contains("lookup", StringComparison.OrdinalIgnoreCase))
            {
                return StubHandler.Json(HttpStatusCode.NotFound, "{}");
            }

            if (method == HttpMethod.Post && path.Contains("customers", StringComparison.OrdinalIgnoreCase)
                && !path.Contains("subscription", StringComparison.OrdinalIgnoreCase))
            {
                return StubHandler.Json(HttpStatusCode.Created, """
                    { "customer": { "id": 42, "reference": "demouser@microsoft.com", "email": "demouser@microsoft.com", "first_name": "Demouser", "last_name": "eShopOnWeb" } }
                    """);
            }

            if (method == HttpMethod.Get && path.Contains("subscription", StringComparison.OrdinalIgnoreCase))
            {
                return StubHandler.Json(HttpStatusCode.OK, "[]");
            }

            if (method == HttpMethod.Post && path.Contains("subscription", StringComparison.OrdinalIgnoreCase))
            {
                return StubHandler.Json(HttpStatusCode.Created, ActiveSubscriptionJson());
            }

            return StubHandler.Json(HttpStatusCode.NotFound, "{}");
        });

        var service = CreateService(handler);

        var result = await service.SubscribeAsync(DemoShopper, "eshop-pro", CancellationToken.None);

        Assert.True(result.Created);
        Assert.Equal("eshop-pro", result.Subscription.PlanHandle);
        Assert.Equal(299.00m, result.Subscription.Price);
        Assert.Equal("active", result.Subscription.State);
        Assert.NotNull(result.Subscription.NextBillingDate);
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.Contains("customer", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.Contains("subscription", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Subscribe_ReturnsExisting_WhenAlreadyEnrolled()
    {
        var createCalls = 0;
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            var method = request.Method;

            if (method == HttpMethod.Get && path.Contains("/products/handle/", StringComparison.OrdinalIgnoreCase))
            {
                return StubHandler.Json(HttpStatusCode.OK, """
                    { "product": { "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900, "interval": 1, "interval_unit": "month" } }
                    """);
            }

            if (method == HttpMethod.Get && path.Contains("lookup", StringComparison.OrdinalIgnoreCase))
            {
                return StubHandler.Json(HttpStatusCode.OK, """
                    { "customer": { "id": 42, "reference": "demouser@microsoft.com", "email": "demouser@microsoft.com" } }
                    """);
            }

            if (method == HttpMethod.Get && path.Contains("subscription", StringComparison.OrdinalIgnoreCase))
            {
                return StubHandler.Json(HttpStatusCode.OK, $"""[ {ActiveSubscriptionJson()} ]""");
            }

            if (method == HttpMethod.Post)
            {
                Interlocked.Increment(ref createCalls);
                return StubHandler.Json(HttpStatusCode.Created, ActiveSubscriptionJson());
            }

            return StubHandler.Json(HttpStatusCode.NotFound, "{}");
        });

        var service = CreateService(handler);

        var result = await service.SubscribeAsync(DemoShopper, "eshop-pro", CancellationToken.None);

        Assert.False(result.Created);
        Assert.Equal(99, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal(0, createCalls);
    }

    [Fact]
    public async Task ListShopperSubscriptions_ReturnsEmpty_WhenCustomerDoesNotExist()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.NotFound, "{}"));
        var service = CreateService(handler);

        var result = await service.ListShopperSubscriptionsAsync(DemoShopper, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Subscribe_ThrowsNotFound_WhenPlanHandleIsUnknown()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.NotFound, "{}"));
        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<BillingException>(
            () => service.SubscribeAsync(DemoShopper, "missing-plan", CancellationToken.None));

        Assert.Equal(404, ex.StatusCode);
    }

    private static MaxioSubscriptionBillingService CreateService(HttpMessageHandler handler)
    {
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), new MaxioAdvancedBillingClientOptions());
        var options = Options.Create(new MaxioOptions { ProductFamilyHandle = "eshop-subscribe" });
        return new MaxioSubscriptionBillingService(client, options, NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    private static string ActiveSubscriptionJson()
    {
        return """
            {
              "subscription": {
                "id": 99,
                "state": "active",
                "product_price_in_cents": 29900,
                "next_assessment_at": "2026-09-21T00:00:00+00:00",
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
    }
}
