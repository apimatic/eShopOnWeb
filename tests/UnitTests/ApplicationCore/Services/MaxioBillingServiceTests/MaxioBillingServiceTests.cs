using System;
using System.Collections.Generic;
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

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.MaxioBillingServiceTests;

public class MaxioOptionsResolveBaseUrl
{
    [Fact]
    public void UsesBaseUrlVerbatimWhenSet()
    {
        var options = new MaxioOptions
        {
            Subdomain = "ignored",
            BaseUrl = "https://billing.example.test/v1"
        };

        Assert.Equal("https://billing.example.test/v1/", options.ResolveBaseUrl("EU"));
    }

    [Fact]
    public void DerivesUsHostFromSubdomain()
    {
        var options = new MaxioOptions { Subdomain = "cp-exp-4" };
        Assert.Equal("https://cp-exp-4.chargify.com/", options.ResolveBaseUrl("US"));
    }

    [Fact]
    public void DerivesEuHostFromSubdomain()
    {
        var options = new MaxioOptions { Subdomain = "acme" };
        Assert.Equal("https://acme.ebilling.maxio.com/", options.ResolveBaseUrl("EU"));
    }
}

public class MaxioApiClientErrorMapping
{
    [Fact]
    public void ExtractsArrayErrors()
    {
        var detail = MaxioApiClient.ExtractErrorDetail("""{"errors":["Product must be specified"]}""");
        Assert.Equal("Product must be specified", detail);
    }

    [Fact]
    public void MapsUnauthorizedToServiceUnavailable()
    {
        var ex = MaxioApiClient.MapError(HttpStatusCode.Unauthorized, "{}");
        Assert.Equal(503, ex.StatusCode);
    }
}

public class SubscribeIdempotency
{
    [Fact]
    public async Task ListPlansMapsFamilyProducts()
    {
        var handler = new ScriptedHandler(request =>
        {
            Assert.Contains("product_families/handle:eshop-subscribe/products.json", request.RequestUri!.ToString());
            return Json(200, """
                [
                  {
                    "product": {
                      "id": 1,
                      "name": "Pro Plan",
                      "handle": "eshop-pro",
                      "description": "Default plan",
                      "price_in_cents": 29900,
                      "interval": 1,
                      "interval_unit": "month",
                      "archived_at": null,
                      "product_family": { "id": 9, "handle": "eshop-subscribe", "name": "eShop" }
                    }
                  },
                  {
                    "product": {
                      "id": 2,
                      "name": "Archived",
                      "handle": "old",
                      "price_in_cents": 100,
                      "interval": 1,
                      "interval_unit": "month",
                      "archived_at": "2020-01-01T00:00:00Z"
                    }
                  }
                ]
                """);
        });

        var service = CreateService(handler);
        var plans = await service.ListPlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal(299.00m, plan.Price);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.Equal("month", plan.IntervalUnit);
    }

    [Fact]
    public async Task SubscribeReturnsExistingLiveSubscriptionWithoutCreating()
    {
        var posts = 0;
        var handler = new ScriptedHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/products.json"))
            {
                return Json(200, """
                    [{"product":{"id":1,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month","archived_at":null}}]
                    """);
            }

            if (path.Contains("/customers/lookup.json"))
            {
                return Json(200, """{"customer":{"id":42,"email":"demouser@microsoft.com","reference":"user-1"}}""");
            }

            if (path.Contains("/customers/42/subscriptions.json"))
            {
                return Json(200, """
                    [{"subscription":{
                        "id":99,
                        "state":"active",
                        "reference":"user-1:eshop-pro",
                        "product_price_in_cents":29900,
                        "next_assessment_at":"2026-09-19T00:00:00Z",
                        "product":{"id":1,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900},
                        "customer":{"id":42}
                    }}]
                    """);
            }

            if (request.Method == HttpMethod.Post)
            {
                posts++;
            }

            return Json(500, """{"errors":["unexpected"]}""");
        });

        var service = CreateService(handler);
        var result = await service.SubscribeAsync(new SubscribeCommand
        {
            UserId = "user-1",
            Email = "demouser@microsoft.com",
            FirstName = "Demo",
            LastName = "Shopper",
            ProductHandle = "eshop-pro"
        });

        Assert.False(result.Created);
        Assert.Equal(99, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal(299.00m, result.Subscription.Price);
        Assert.Equal(0, posts);
    }

    [Fact]
    public async Task SubscribeCreatesCustomerAndSubscription()
    {
        var createdCustomers = 0;
        var createdSubscriptions = 0;
        var handler = new ScriptedHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/products.json"))
            {
                return Json(200, """
                    [{"product":{"id":1,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month"}}]
                    """);
            }

            if (path.Contains("/customers/lookup.json"))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("""{"errors":["Not Found"]}""", Encoding.UTF8, "application/json")
                };
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/customers.json"))
            {
                createdCustomers++;
                return Json(201, """{"customer":{"id":7,"email":"a@b.c","reference":"user-2"}}""");
            }

            if (path.Contains("/customers/7/subscriptions.json"))
            {
                return Json(200, "[]");
            }

            if (path.Contains("/subscriptions/lookup.json"))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                };
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json"))
            {
                createdSubscriptions++;
                return Json(201, """
                    {"subscription":{
                        "id":15,
                        "state":"active",
                        "reference":"user-2:eshop-pro",
                        "product_price_in_cents":29900,
                        "next_assessment_at":"2026-09-19T00:00:00Z",
                        "product":{"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900},
                        "customer":{"id":7}
                    }}
                    """);
            }

            return Json(500, """{"errors":["unexpected path"]}""");
        });

        var service = CreateService(handler);
        var result = await service.SubscribeAsync(new SubscribeCommand
        {
            UserId = "user-2",
            Email = "a@b.c",
            FirstName = "A",
            LastName = "B",
            ProductHandle = "eshop-pro"
        });

        Assert.True(result.Created);
        Assert.Equal(15, result.Subscription.Id);
        Assert.Equal("eshop-pro", result.Subscription.ProductHandle);
        Assert.Equal(1, createdCustomers);
        Assert.Equal(1, createdSubscriptions);
    }

    [Fact]
    public async Task SubscribeRejectsUnknownPlan()
    {
        var handler = new ScriptedHandler(_ => Json(200, """
            [{"product":{"id":1,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month"}}]
            """));

        var service = CreateService(handler);
        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(() => service.SubscribeAsync(new SubscribeCommand
        {
            UserId = "user-1",
            Email = "a@b.c",
            FirstName = "A",
            LastName = "B",
            ProductHandle = "not-a-plan"
        }));
    }

    [Fact]
    public async Task ListSubscriptionsReturnsEmptyWhenCustomerMissing()
    {
        var handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        });

        var service = CreateService(handler);
        var result = await service.ListSubscriptionsForCustomerAsync("nobody");
        Assert.Empty(result);
    }

    private static MaxioBillingService CreateService(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://cp-exp-4.chargify.com/") };
        var api = new MaxioApiClient(http);
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "cp-exp-4",
            ProductFamilyHandle = "eshop-subscribe"
        });
        return new MaxioBillingService(api, options, NullLogger<MaxioBillingService>.Instance);
    }

    private static HttpResponseMessage Json(int status, string json)
    {
        return new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }
}
