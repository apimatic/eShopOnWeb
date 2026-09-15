using System.Net;
using System.Net.Http;
using System.Text;
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
    private static readonly Shopper DemoShopper = new("user-123", "demouser@microsoft.com", "demouser@microsoft.com");

    [Fact]
    public async Task ListAvailablePlans_MapsFamilyProducts()
    {
        var handler = new ScriptedHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Contains("/product_families/handle:eshop-subscribe/products.json", request.RequestUri!.ToString());
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
                      "interval_unit": "month"
                    }
                  }
                ]
                """);
        });

        var service = CreateService(handler);

        var plans = await service.ListAvailablePlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.Equal("month", plan.IntervalUnit);
    }

    [Fact]
    public async Task Subscribe_CreatesCustomerThenSubscription()
    {
        var handler = new ScriptedHandler((request, body) =>
        {
            var path = request.RequestUri!.PathAndQuery;

            if (path.Contains("/product_families/handle:eshop-subscribe/products.json"))
            {
                return FamilyProducts();
            }

            if (path.StartsWith("/customers/lookup.json"))
            {
                return Json(HttpStatusCode.NotFound, """{"errors":["Not Found"]}""");
            }

            if (request.Method == HttpMethod.Post && path == "/customers.json")
            {
                Assert.Contains("\"reference\":\"user-123\"", body);
                Assert.Contains("uniqueness_token", body);
                return Json(HttpStatusCode.OK, """
                    { "customer": { "id": 44, "email": "demouser@microsoft.com", "reference": "user-123" } }
                    """);
            }

            if (path == "/customers/44/subscriptions.json")
            {
                return Json(HttpStatusCode.OK, "[]");
            }

            if (request.Method == HttpMethod.Post && path == "/subscriptions.json")
            {
                Assert.Contains("\"product_handle\":\"eshop-pro\"", body);
                Assert.Contains("\"customer_id\":44", body);
                Assert.Contains("\"customer_reference\":\"user-123\"", body);
                Assert.Contains("\"payment_collection_method\":\"remittance\"", body);
                Assert.Contains("uniqueness_token", body);
                return Json(HttpStatusCode.Created, """
                    {
                      "subscription": {
                        "id": 99,
                        "state": "active",
                        "product_price_in_cents": 29900,
                        "next_assessment_at": "2026-09-20T00:00:00Z",
                        "product": { "id": 1, "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900, "interval": 1, "interval_unit": "month" }
                      }
                    }
                    """);
            }

            throw new InvalidOperationException($"Unexpected request {request.Method} {path}");
        });

        var service = CreateService(handler);

        var subscription = await service.SubscribeAsync(DemoShopper, "eshop-pro");

        Assert.Equal(99, subscription.Id);
        Assert.Equal("active", subscription.State);
        Assert.Equal("eshop-pro", subscription.ProductHandle);
        Assert.Equal(29900, subscription.PriceInCents);
        Assert.Equal(DateTimeOffset.Parse("2026-09-20T00:00:00Z"), subscription.NextBillingAt);
    }

    [Fact]
    public async Task Subscribe_IsIdempotentWhenLiveSubscriptionExists()
    {
        var createdSubscriptions = 0;
        var handler = new ScriptedHandler((request, _) =>
        {
            var path = request.RequestUri!.PathAndQuery;

            if (path.Contains("/product_families/handle:eshop-subscribe/products.json"))
            {
                return FamilyProducts();
            }

            if (path.StartsWith("/customers/lookup.json"))
            {
                return Json(HttpStatusCode.OK, """
                    { "customer": { "id": 44, "email": "demouser@microsoft.com", "reference": "user-123" } }
                    """);
            }

            if (path == "/customers/44/subscriptions.json")
            {
                return Json(HttpStatusCode.OK, """
                    [
                      {
                        "subscription": {
                          "id": 99,
                          "state": "active",
                          "product_price_in_cents": 29900,
                          "next_assessment_at": "2026-09-20T00:00:00Z",
                          "product": { "id": 1, "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900, "interval": 1, "interval_unit": "month" }
                        }
                      }
                    ]
                    """);
            }

            if (request.Method == HttpMethod.Post && path == "/subscriptions.json")
            {
                createdSubscriptions++;
            }

            throw new InvalidOperationException($"Unexpected request {request.Method} {path}");
        });

        var service = CreateService(handler);

        var first = await service.SubscribeAsync(DemoShopper, "eshop-pro");
        var second = await service.SubscribeAsync(DemoShopper, "eshop-pro");

        Assert.Equal(99, first.Id);
        Assert.Equal(99, second.Id);
        Assert.Equal(0, createdSubscriptions);
    }

    [Fact]
    public async Task Subscribe_RecoversCustomerWhenCreateConflicts()
    {
        var createdCustomers = 0;
        var handler = new ScriptedHandler((request, _) =>
        {
            var path = request.RequestUri!.PathAndQuery;

            if (path.Contains("/product_families/handle:eshop-subscribe/products.json"))
            {
                return FamilyProducts();
            }

            if (path.StartsWith("/customers/lookup.json"))
            {
                if (createdCustomers == 0)
                {
                    return Json(HttpStatusCode.NotFound, """{"errors":["Not Found"]}""");
                }

                return Json(HttpStatusCode.OK, """
                    { "customer": { "id": 44, "email": "demouser@microsoft.com", "reference": "user-123" } }
                    """);
            }

            if (request.Method == HttpMethod.Post && path == "/customers.json")
            {
                createdCustomers++;
                return Json(HttpStatusCode.UnprocessableEntity, """
                    { "errors": { "customer": "reference: must be unique - that value has been taken." } }
                    """);
            }

            if (path == "/customers/44/subscriptions.json")
            {
                return Json(HttpStatusCode.OK, "[]");
            }

            if (request.Method == HttpMethod.Post && path == "/subscriptions.json")
            {
                return Json(HttpStatusCode.Created, """
                    {
                      "subscription": {
                        "id": 77,
                        "state": "active",
                        "product_price_in_cents": 2900,
                        "product": { "id": 2, "name": "Basic Plan", "handle": "basic-plan", "price_in_cents": 2900, "interval": 1, "interval_unit": "month" }
                      }
                    }
                    """);
            }

            throw new InvalidOperationException($"Unexpected request {request.Method} {path}");
        });

        var service = CreateService(handler);

        var subscription = await service.SubscribeAsync(DemoShopper, "basic-plan");

        Assert.Equal(77, subscription.Id);
        Assert.Equal(1, createdCustomers);
    }

    [Fact]
    public async Task Subscribe_RejectsUnknownPlanHandle()
    {
        var handler = new ScriptedHandler((_, _) => FamilyProducts());
        var service = CreateService(handler);

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(() =>
            service.SubscribeAsync(DemoShopper, "not-a-plan"));
    }

    [Fact]
    public async Task ListShopperSubscriptions_ReturnsEmptyWhenCustomerDoesNotExist()
    {
        var handler = new ScriptedHandler((request, _) =>
        {
            Assert.Contains("/customers/lookup.json", request.RequestUri!.ToString());
            return Json(HttpStatusCode.NotFound, """{"errors":["Not Found"]}""");
        });

        var service = CreateService(handler);

        var subscriptions = await service.ListShopperSubscriptionsAsync(DemoShopper);

        Assert.Empty(subscriptions);
    }

    private static MaxioSubscriptionBillingService CreateService(ScriptedHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example.chargify.com/") };
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "example",
            ProductFamilyHandle = FamilyHandle
        });

        return new MaxioSubscriptionBillingService(
            httpClient,
            options,
            NullLogger<MaxioSubscriptionBillingService>.Instance,
            new SubscribeIdempotencyGate());
    }

    private static HttpResponseMessage FamilyProducts() => Json(HttpStatusCode.OK, """
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

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string json) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string?, HttpResponseMessage> _handler;

        public ScriptedHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return _handler(request, body);
        }
    }
}
