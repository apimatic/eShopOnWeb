using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioBillingServiceTests
{
    private static readonly ShopperIdentity Shopper = new("user-1", "demouser@microsoft.com", "demouser@microsoft.com");

    [Fact]
    public async Task ListPlansAsync_MapsProductsInTheConfiguredFamily()
    {
        var handler = new ScriptedHandler((request, _) =>
        {
            Assert.EndsWith("/product_families/handle:eshop-subscribe/products.json?page=1&per_page=200", request.RequestUri!.ToString());
            return Json(HttpStatusCode.OK, """
                [
                  {
                    "product": {
                      "id": 1,
                      "name": "Pro Plan",
                      "handle": "eshop-pro",
                      "description": "Pro",
                      "price_in_cents": 29900,
                      "interval": 1,
                      "interval_unit": "month",
                      "require_credit_card": false,
                      "archived_at": null
                    }
                  }
                ]
                """);
        });

        var service = CreateService(handler);

        var plans = await service.ListPlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal("Pro Plan", plan.Name);
        Assert.Equal(299.00m, plan.Price);
        Assert.Equal("month", plan.IntervalUnit);
        Assert.False(plan.PaymentMethodRequired);
    }

    [Fact]
    public async Task SubscribeAsync_IsIdempotentForAnOpenPlan()
    {
        var subscriptionPosts = 0;
        var handler = new ScriptedHandler((request, _) =>
        {
            var path = request.RequestUri!.PathAndQuery;
            if (request.Method == HttpMethod.Get && path.StartsWith("/products/handle/eshop-pro.json"))
            {
                return Json(HttpStatusCode.OK, ProductJson());
            }

            if (request.Method == HttpMethod.Get && path.StartsWith("/customers/lookup.json"))
            {
                return Json(HttpStatusCode.OK, CustomerJson());
            }

            if (request.Method == HttpMethod.Get && path.StartsWith("/customers/42/subscriptions.json"))
            {
                return Json(HttpStatusCode.OK, subscriptionPosts == 0 ? "[]" : $"[{SubscriptionJson()}]");
            }

            if (request.Method == HttpMethod.Post && path.StartsWith("/subscriptions.json"))
            {
                subscriptionPosts++;
                return Json(HttpStatusCode.Created, SubscriptionJson());
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var service = CreateService(handler);

        var first = await service.SubscribeAsync(Shopper, "eshop-pro");
        var second = await service.SubscribeAsync(Shopper, "eshop-pro");

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(1, subscriptionPosts);
        Assert.Equal(1001, first.Subscription.Id);
        Assert.Equal("eshop-pro", second.Subscription.ProductHandle);
        Assert.Equal("active", second.Subscription.State);
        Assert.Equal(299.00m, first.Subscription.Price);
    }

    [Fact]
    public async Task ListSubscriptionsAsync_ReturnsEmptyWhenCustomerDoesNotExist()
    {
        var handler = new ScriptedHandler((request, _) =>
        {
            Assert.Contains("/customers/lookup.json?reference=user-1", request.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("not found")
            };
        });

        var service = CreateService(handler);

        var subscriptions = await service.ListSubscriptionsAsync(Shopper);

        Assert.Empty(subscriptions);
    }

    private static MaxioBillingService CreateService(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://cp-exp-2.chargify.com/")
        };

        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "cp-exp-2",
            ProductFamilyHandle = "eshop-subscribe"
        });

        return new MaxioBillingService(httpClient, options, NullLogger<MaxioBillingService>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static string ProductJson() => """
        {
          "product": {
            "id": 1,
            "name": "Pro Plan",
            "handle": "eshop-pro",
            "price_in_cents": 29900,
            "interval": 1,
            "interval_unit": "month",
            "require_credit_card": false,
            "product_family": { "handle": "eshop-subscribe" }
          }
        }
        """;

    private static string CustomerJson() => """
        {
          "customer": {
            "id": 42,
            "first_name": "Demouser",
            "last_name": "Customer",
            "email": "demouser@microsoft.com",
            "reference": "user-1"
          }
        }
        """;

    private static string SubscriptionJson() => """
        {
          "subscription": {
            "id": 1001,
            "state": "active",
            "product_price_in_cents": 29900,
            "next_assessment_at": "2026-09-20T12:00:00-04:00",
            "created_at": "2026-08-20T12:00:00-04:00",
            "product": {
              "name": "Pro Plan",
              "handle": "eshop-pro",
              "interval": 1,
              "interval_unit": "month",
              "price_in_cents": 29900
            }
          }
        }
        """;

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _handler;

        public ScriptedHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request, cancellationToken));
        }
    }
}
