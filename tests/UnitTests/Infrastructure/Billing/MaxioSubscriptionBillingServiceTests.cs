using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
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
    private static readonly ShopperIdentity Shopper = new("user-1", "demouser@microsoft.com", "Demouser", "User");

    [Fact]
    public async Task ListPlansAsync_ReturnsProductsForConfiguredFamily()
    {
        var handler = new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            var uri = Uri.UnescapeDataString(request.RequestUri!.AbsoluteUri);
            Assert.Contains("eshop-subscribe", uri, StringComparison.Ordinal);
            Assert.Contains("product_famil", uri, StringComparison.OrdinalIgnoreCase);
            return StubHandler.Json(HttpStatusCode.OK, """
                [
                  {
                    "product": {
                      "id": 1,
                      "name": "Pro Plan",
                      "handle": "eshop-pro",
                      "description": "Pro",
                      "price_in_cents": 29900,
                      "interval": 1,
                      "interval_unit": "month"
                    }
                  },
                  {
                    "product": {
                      "id": 2,
                      "name": "Basic Plan",
                      "handle": "basic-plan",
                      "description": "Basic",
                      "price_in_cents": 2900,
                      "interval": 1,
                      "interval_unit": "month"
                    }
                  }
                ]
                """);
        });

        var service = CreateService(handler);

        var plans = await service.ListPlansAsync(default);

        Assert.Equal(2, plans.Count);
        Assert.Contains(plans, p => p.Handle == "eshop-pro" && p.Price == 299.00m);
        Assert.Contains(plans, p => p.Handle == "basic-plan" && p.Price == 29.00m);
    }

    [Fact]
    public async Task SubscribeAsync_ReturnsExistingActiveSubscriptionWithoutCreatingAnother()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.Contains("lookup", StringComparison.OrdinalIgnoreCase))
            {
                return CustomerJson();
            }

            if (request.Method == HttpMethod.Get && path.Contains("subscriptions", StringComparison.OrdinalIgnoreCase))
            {
                return SubscriptionListJson("eshop-pro", "active");
            }

            throw new InvalidOperationException($"Unexpected request {request.Method} {request.RequestUri}");
        });

        var service = CreateService(handler);

        var result = await service.SubscribeAsync(Shopper, "eshop-pro", default);

        Assert.False(result.Created);
        Assert.Equal("eshop-pro", result.Subscription.ProductHandle);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal(299.00m, result.Subscription.Price);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerAndSubscriptionWhenMissing()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.Contains("lookup", StringComparison.OrdinalIgnoreCase))
            {
                return StubHandler.Json(HttpStatusCode.NotFound, """{"errors":"Not Found"}""");
            }

            if (request.Method == HttpMethod.Post && path.Contains("customers", StringComparison.OrdinalIgnoreCase))
            {
                return CustomerJson();
            }

            if (request.Method == HttpMethod.Get && path.Contains("subscriptions", StringComparison.OrdinalIgnoreCase))
            {
                return StubHandler.Json(HttpStatusCode.OK, "[]");
            }

            if (request.Method == HttpMethod.Post && path.Contains("subscriptions", StringComparison.OrdinalIgnoreCase))
            {
                return StubHandler.Json(HttpStatusCode.Created, """
                    {
                      "subscription": {
                        "id": 99,
                        "state": "active",
                        "product": { "handle": "eshop-pro", "name": "Pro Plan" },
                        "product_price_in_cents": 29900,
                        "current_period_ends_at": "2026-10-03T00:00:00Z"
                      }
                    }
                    """);
            }

            throw new InvalidOperationException($"Unexpected request {request.Method} {request.RequestUri}");
        });

        var service = CreateService(handler);

        var result = await service.SubscribeAsync(Shopper, "eshop-pro", default);

        Assert.True(result.Created);
        Assert.Equal(99, result.Subscription.Id);
        Assert.Equal("eshop-pro", result.Subscription.ProductHandle);
        Assert.Equal(new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero), result.Subscription.NextBillingDate);
        Assert.Equal(1, handler.Requests.Count(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.Contains("customers")));
        Assert.Equal(1, handler.Requests.Count(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.Contains("subscriptions")));
    }

    [Fact]
    public async Task ListMySubscriptionsAsync_ReturnsEmptyWhenCustomerIsMissing()
    {
        var handler = new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            return StubHandler.Json(HttpStatusCode.NotFound, """{"errors":"Not Found"}""");
        });

        var service = CreateService(handler);

        var subscriptions = await service.ListMySubscriptionsAsync(Shopper, default);

        Assert.Empty(subscriptions);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task SubscribeAsync_DoesNotResendPostOnTransportRetry()
    {
        var posts = 0;
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.Contains("lookup", StringComparison.OrdinalIgnoreCase))
            {
                return CustomerJson();
            }

            if (request.Method == HttpMethod.Get && path.Contains("subscriptions", StringComparison.OrdinalIgnoreCase))
            {
                return StubHandler.Json(HttpStatusCode.OK, "[]");
            }

            if (request.Method == HttpMethod.Post)
            {
                posts++;
                throw new HttpRequestException("connection reset");
            }

            throw new InvalidOperationException($"Unexpected request {request.Method} {request.RequestUri}");
        });

        var httpClient = new HttpClient(new OnceOnlyWriteHandler { InnerHandler = handler });
        var service = CreateService(httpClient);

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() =>
            service.SubscribeAsync(Shopper, "eshop-pro", default));

        Assert.Equal(503, ex.StatusCode);
        Assert.Equal(1, posts);
    }

    private static MaxioSubscriptionBillingService CreateService(StubHandler handler) =>
        CreateService(new HttpClient(new OnceOnlyWriteHandler { InnerHandler = new LastStatusCaptureHandler { InnerHandler = handler } }));

    private static MaxioSubscriptionBillingService CreateService(HttpClient httpClient)
    {
        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials
            {
                Username = "test-key",
                Password = "x"
            }
        };
        clientOptions.Server.Production.Us.Site = "test-site";
        var client = new MaxioAdvancedBillingClient(httpClient, clientOptions);
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = "eshop-subscribe"
        });
        return new MaxioSubscriptionBillingService(client, options, NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    private static HttpResponseMessage CustomerJson() =>
        StubHandler.Json(HttpStatusCode.OK, """
            {
              "customer": {
                "id": 42,
                "reference": "user-1",
                "first_name": "Demouser",
                "last_name": "User",
                "email": "demouser@microsoft.com"
              }
            }
            """);

    private static HttpResponseMessage SubscriptionListJson(string handle, string state) =>
        StubHandler.Json(HttpStatusCode.OK, $$"""
            [
              {
                "subscription": {
                  "id": 77,
                  "state": "{{state}}",
                  "product": { "handle": "{{handle}}", "name": "Pro Plan" },
                  "product_price_in_cents": 29900,
                  "current_period_ends_at": "2026-10-03T00:00:00Z"
                }
              }
            ]
            """);
}
