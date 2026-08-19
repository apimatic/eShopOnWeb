using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioBillingServiceTests
{
    private static readonly ShopperIdentity Shopper =
        new("user-123", "demouser@microsoft.com", "demouser@microsoft.com");

    [Fact]
    public void ResolveBaseUrl_UsesBaseUrlVerbatimWhenSet()
    {
        var options = new MaxioOptions
        {
            Subdomain = "ignored-subdomain",
            BaseUrl = "https://billing.example.test/v1"
        };

        Assert.Equal("https://billing.example.test/v1/", MaxioBillingService.ResolveBaseUrl(options));
    }

    [Fact]
    public void ResolveBaseUrl_DerivesChargifyHostFromSubdomain()
    {
        var options = new MaxioOptions { Subdomain = "cp-exp-3" };

        Assert.Equal("https://cp-exp-3.chargify.com/", MaxioBillingService.ResolveBaseUrl(options));
    }

    [Fact]
    public async Task ListPlansAsync_ReturnsActiveProductsInFamily()
    {
        var handler = new ScriptedHandler
        {
            Responder = request =>
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
                          "name": "Archived",
                          "handle": "old-plan",
                          "price_in_cents": 100,
                          "interval": 1,
                          "interval_unit": "month",
                          "archived_at": "2020-01-01T00:00:00Z"
                        }
                      }
                    ]
                    """);
            }
        };

        var sut = CreateSut(handler);
        var plans = await sut.ListPlansAsync();

        Assert.Single(plans);
        Assert.Equal("eshop-pro", plans[0].Handle);
        Assert.Equal(299.00m, plans[0].Price);
        Assert.Equal("month", plans[0].IntervalUnit);
    }

    [Fact]
    public async Task ListSubscriptionsAsync_ReturnsEmptyWhenCustomerDoesNotExist()
    {
        var handler = new ScriptedHandler
        {
            Responder = request =>
            {
                Assert.Contains("customers/lookup.json", request.RequestUri!.ToString());
                Assert.Contains("reference=user-123", request.RequestUri!.Query);
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        };

        var sut = CreateSut(handler);
        var result = await sut.ListSubscriptionsAsync(Shopper);

        Assert.Empty(result);
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerThenSubscription()
    {
        var handler = new ScriptedHandler
        {
            Responder = request =>
            {
                var url = request.RequestUri!.ToString();
                if (request.Method == HttpMethod.Get && url.Contains("customers/lookup.json"))
                {
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }

                if (request.Method == HttpMethod.Post && url.EndsWith("customers.json"))
                {
                    return Json(HttpStatusCode.OK, """
                        { "customer": { "id": 55, "email": "demouser@microsoft.com", "reference": "user-123" } }
                        """);
                }

                if (request.Method == HttpMethod.Get && url.Contains("product_families/"))
                {
                    return Json(HttpStatusCode.OK, """
                        [{ "product": { "id": 1, "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900, "interval": 1, "interval_unit": "month" } }]
                        """);
                }

                if (request.Method == HttpMethod.Get && url.Contains("/subscriptions.json"))
                {
                    return Json(HttpStatusCode.OK, "[]");
                }

                if (request.Method == HttpMethod.Post && url.EndsWith("subscriptions.json"))
                {
                    return Json(HttpStatusCode.Created, """
                        {
                          "subscription": {
                            "id": 9001,
                            "state": "active",
                            "product_price_in_cents": 29900,
                            "current_period_ends_at": "2026-09-19T00:00:00Z",
                            "product": { "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900 }
                          }
                        }
                        """);
                }

                return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent($"Unexpected {request.Method} {url}")
                };
            }
        };

        var sut = CreateSut(handler);
        var result = await sut.SubscribeAsync(Shopper, "eshop-pro");

        Assert.Equal(9001, result.Id);
        Assert.Equal("active", result.State);
        Assert.Equal("eshop-pro", result.ProductHandle);
        Assert.Equal(299.00m, result.Price);
        Assert.False(result.AlreadyExisted);
        Assert.Equal(new DateTimeOffset(2026, 9, 19, 0, 0, 0, TimeSpan.Zero), result.NextBillingDate);
    }

    [Fact]
    public async Task SubscribeAsync_IsIdempotentWhenLiveSubscriptionExists()
    {
        var created = 0;
        var handler = new ScriptedHandler
        {
            Responder = request =>
            {
                var url = request.RequestUri!.ToString();
                if (url.Contains("product_families/"))
                {
                    return Json(HttpStatusCode.OK, """
                        [{ "product": { "id": 1, "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900, "interval": 1, "interval_unit": "month" } }]
                        """);
                }

                if (url.Contains("customers/lookup.json"))
                {
                    return Json(HttpStatusCode.OK, """{ "customer": { "id": 55, "reference": "user-123" } }""");
                }

                if (request.Method == HttpMethod.Get && url.Contains("/subscriptions.json"))
                {
                    return Json(HttpStatusCode.OK, """
                        [{
                          "subscription": {
                            "id": 9001,
                            "state": "active",
                            "product_price_in_cents": 29900,
                            "current_period_ends_at": "2026-09-19T00:00:00Z",
                            "product": { "handle": "eshop-pro", "name": "Pro Plan" }
                          }
                        }]
                        """);
                }

                if (request.Method == HttpMethod.Post && url.EndsWith("subscriptions.json"))
                {
                    Interlocked.Increment(ref created);
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError);
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        };

        var sut = CreateSut(handler);
        var first = await sut.SubscribeAsync(Shopper, "eshop-pro");
        var second = await sut.SubscribeAsync(Shopper, "eshop-pro");

        Assert.Equal(9001, first.Id);
        Assert.True(first.AlreadyExisted);
        Assert.Equal(9001, second.Id);
        Assert.Equal(0, created);
    }

    [Fact]
    public async Task SubscribeAsync_ThrowsWhenPlanIsUnknown()
    {
        var handler = new ScriptedHandler
        {
            Responder = _ => Json(HttpStatusCode.OK, """
                [{ "product": { "id": 1, "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900, "interval": 1, "interval_unit": "month" } }]
                """)
        };

        var sut = CreateSut(handler);
        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(() => sut.SubscribeAsync(Shopper, "not-a-plan"));
    }

    private static MaxioBillingService CreateSut(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.chargify.com/") };
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "example",
            ProductFamilyHandle = "eshop-subscribe"
        });
        return new MaxioBillingService(http, options, NullLogger<MaxioBillingService>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public required Func<HttpRequestMessage, HttpResponseMessage> Responder { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Responder(request));
    }
}
