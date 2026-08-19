using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Entities.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioBillingServiceTests
{
    [Fact]
    public async Task ListPlansAsync_MapsFamilyProductsAndSkipsArchived()
    {
        var handler = new ScriptedHandler((request) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Contains("product_families/handle:eshop-subscribe/products.json", request.RequestUri!.ToString());
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
                      "archived_at": null
                    }
                  },
                  {
                    "product": {
                      "id": 2,
                      "name": "Gone",
                      "handle": "archived-plan",
                      "price_in_cents": 100,
                      "interval": 1,
                      "interval_unit": "month",
                      "archived_at": "2024-01-01T00:00:00Z"
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
        Assert.Equal(29900, plan.PriceInCents);
        Assert.Equal("month", plan.IntervalUnit);
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerThenSubscription()
    {
        var handler = new ScriptedHandler(request =>
        {
            var path = request.RequestUri!.ToString();
            if (path.Contains("/products/handle/eshop-pro.json"))
            {
                return Json(HttpStatusCode.OK, """
                    { "product": { "id": 1, "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900, "interval": 1, "interval_unit": "month", "product_family": { "handle": "eshop-subscribe" } } }
                    """);
            }

            if (path.Contains("/subscriptions/lookup.json"))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (path.Contains("/customers/lookup.json"))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/customers.json"))
            {
                return Json(HttpStatusCode.OK, """
                    { "customer": { "id": 42, "email": "demouser@microsoft.com", "reference": "user-1", "first_name": "demouser", "last_name": "eShopOnWeb" } }
                    """);
            }

            if (path.Contains("/customers/42/subscriptions.json"))
            {
                return Json(HttpStatusCode.OK, "[]");
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json"))
            {
                return Json(HttpStatusCode.Created, """
                    {
                      "subscription": {
                        "id": 99,
                        "state": "active",
                        "product_price_in_cents": 29900,
                        "next_assessment_at": "2026-09-20T00:00:00Z",
                        "product": { "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900 }
                      }
                    }
                    """);
            }

            throw new InvalidOperationException($"Unexpected request {request.Method} {path}");
        });

        var service = CreateService(handler);
        var result = await service.SubscribeAsync(new SubscribeRequest("user-1", "demouser@microsoft.com", "demouser@microsoft.com", "eshop-pro"));

        Assert.True(result.Created);
        Assert.Equal(99, result.Subscription.Id);
        Assert.Equal("eshop-pro", result.Subscription.ProductHandle);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal(299.00m, result.Subscription.Price);
        Assert.Equal(new DateTimeOffset(2026, 9, 20, 0, 0, 0, TimeSpan.Zero), result.Subscription.NextBillingAt);
    }

    [Fact]
    public async Task SubscribeAsync_IsIdempotentWhenSubscriptionReferenceExists()
    {
        var createCalls = 0;
        var handler = new ScriptedHandler(request =>
        {
            var path = request.RequestUri!.ToString();
            if (path.Contains("/products/handle/eshop-pro.json"))
            {
                return Json(HttpStatusCode.OK, """
                    { "product": { "id": 1, "name": "Pro Plan", "handle": "eshop-pro", "product_family": { "handle": "eshop-subscribe" } } }
                    """);
            }

            if (path.Contains("/subscriptions/lookup.json"))
            {
                return Json(HttpStatusCode.OK, """
                    {
                      "subscription": {
                        "id": 77,
                        "state": "active",
                        "product_price_in_cents": 29900,
                        "product": { "name": "Pro Plan", "handle": "eshop-pro" }
                      }
                    }
                    """);
            }

            if (request.Method == HttpMethod.Post)
            {
                createCalls++;
            }

            throw new InvalidOperationException($"Unexpected request {request.Method} {path}");
        });

        var service = CreateService(handler);
        var result = await service.SubscribeAsync(new SubscribeRequest("user-1", "a@b.com", "a@b.com", "eshop-pro"));

        Assert.False(result.Created);
        Assert.Equal(77, result.Subscription.Id);
        Assert.Equal(0, createCalls);
    }

    [Fact]
    public async Task SubscribeAsync_ReusesCustomerWhenCreateConflicts()
    {
        var customerCreates = 0;
        var handler = new ScriptedHandler(request =>
        {
            var path = request.RequestUri!.ToString();
            if (path.Contains("/products/handle/basic-plan.json"))
            {
                return Json(HttpStatusCode.OK, """
                    { "product": { "id": 2, "name": "Basic Plan", "handle": "basic-plan", "product_family": { "handle": "eshop-subscribe" } } }
                    """);
            }

            if (path.Contains("/subscriptions/lookup.json"))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (path.Contains("/customers/lookup.json"))
            {
                if (customerCreates == 0)
                {
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }

                return Json(HttpStatusCode.OK, """
                    { "customer": { "id": 7, "reference": "user-1", "email": "a@b.com" } }
                    """);
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/customers.json"))
            {
                customerCreates++;
                return Json(HttpStatusCode.UnprocessableEntity, """{ "errors": ["Reference must be unique"] }""");
            }

            if (path.Contains("/customers/7/subscriptions.json"))
            {
                return Json(HttpStatusCode.OK, "[]");
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json"))
            {
                return Json(HttpStatusCode.Created, """
                    { "subscription": { "id": 8, "state": "active", "product_price_in_cents": 2900, "product": { "handle": "basic-plan", "name": "Basic Plan" } } }
                    """);
            }

            throw new InvalidOperationException($"Unexpected request {request.Method} {path}");
        });

        var service = CreateService(handler);
        var result = await service.SubscribeAsync(new SubscribeRequest("user-1", "a@b.com", "a@b.com", "basic-plan"));

        Assert.True(result.Created);
        Assert.Equal(8, result.Subscription.Id);
        Assert.Equal(1, customerCreates);
    }

    [Fact]
    public async Task ListMySubscriptionsAsync_ReturnsEmptyWhenCustomerMissing()
    {
        var handler = new ScriptedHandler(request =>
        {
            Assert.Contains("/customers/lookup.json", request.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var service = CreateService(handler);
        var subscriptions = await service.ListMySubscriptionsAsync("missing-user");

        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task SubscribeAsync_RejectsUnknownPlan()
    {
        var handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<BillingException>(() =>
            service.SubscribeAsync(new SubscribeRequest("user-1", "a@b.com", "a@b.com", "nope")));

        Assert.Contains("Unknown subscription plan", ex.Message);
    }

    [Fact]
    public void SplitDisplayName_UsesEmailLocalPart()
    {
        var (first, last) = MaxioBillingService.SplitDisplayName("demouser@microsoft.com", "ignored");
        Assert.Equal("demouser", first);
        Assert.Equal("eShopOnWeb", last);
    }

    [Fact]
    public void BuildSubscriptionReference_IsStablePerUserAndPlan()
    {
        Assert.Equal("abc:eshop-pro", MaxioBillingService.BuildSubscriptionReference("abc", "eshop-pro"));
    }

    private static MaxioBillingService CreateService(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.chargify.com/")
        };

        var settings = Options.Create(new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "example",
            ProductFamilyHandle = "eshop-subscribe"
        });

        return new MaxioBillingService(http, settings, NullLogger<MaxioBillingService>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responder(request));
        }
    }
}
