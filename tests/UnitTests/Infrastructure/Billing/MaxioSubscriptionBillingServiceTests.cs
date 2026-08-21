using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioSubscriptionBillingServiceTests
{
    private static readonly BillingCustomer Shopper = BillingCustomer.FromUser(
        "user-1",
        "demouser@microsoft.com",
        "demouser@microsoft.com");

    [Fact]
    public async Task ListPlans_ReturnsHandleNamePriceAndInterval()
    {
        var handler = new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Contains("/product_families/", request.RequestUri!.AbsolutePath);
            Assert.Contains("handle:", Uri.UnescapeDataString(request.RequestUri.AbsolutePath));
            Assert.Contains("eshop-subscribe", Uri.UnescapeDataString(request.RequestUri.AbsolutePath));
            return Json(HttpStatusCode.OK, """
                [
                  {
                    "product": {
                      "id": 1,
                      "handle": "eshop-pro",
                      "name": "Pro Plan",
                      "price_in_cents": 29900,
                      "interval": 1,
                      "interval_unit": "month"
                    }
                  },
                  {
                    "product": {
                      "id": 2,
                      "handle": "basic-plan",
                      "name": "Basic Plan",
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
        Assert.Equal("eshop-pro", plans[0].Handle);
        Assert.Equal("Pro Plan", plans[0].Name);
        Assert.Equal(299.00m, plans[0].Price);
        Assert.Equal(1, plans[0].Interval);
        Assert.Equal("month", plans[0].IntervalUnit);
        Assert.Equal("basic-plan", plans[1].Handle);
        Assert.Equal(29.00m, plans[1].Price);
    }

    [Fact]
    public async Task Subscribe_CreatesCustomerAndSubscription_WhenNoneExist()
    {
        var handler = new StubHandler(request => Route(request, lookupStatus: HttpStatusCode.NotFound, subscriptions: "[]"));
        var service = CreateService(handler);

        var result = await service.SubscribeAsync(Shopper, "eshop-pro", default);

        Assert.False(result.AlreadySubscribed);
        Assert.Equal("eshop-pro", result.Subscription.ProductHandle);
        Assert.Equal("Pro Plan", result.Subscription.ProductName);
        Assert.Equal(299.00m, result.Subscription.Price);
        Assert.Equal("active", result.Subscription.State);
        Assert.NotNull(result.Subscription.NextBillingDate);
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.Contains("/customers"));
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.Contains("/subscriptions"));
    }

    [Fact]
    public async Task Subscribe_IsIdempotent_WhenLiveSubscriptionAlreadyExists()
    {
        var existing = """
            [
              {
                "subscription": {
                  "id": 9,
                  "state": "active",
                  "product_price_in_cents": 29900,
                  "next_assessment_at": "2026-09-21T00:00:00Z",
                  "product": { "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900 }
                }
              }
            ]
            """;
        var handler = new StubHandler(request => Route(request, lookupStatus: HttpStatusCode.OK, subscriptions: existing));
        var service = CreateService(handler);

        var result = await service.SubscribeAsync(Shopper, "eshop-pro", default);

        Assert.True(result.AlreadySubscribed);
        Assert.Equal("eshop-pro", result.Subscription.ProductHandle);
        Assert.Equal("active", result.Subscription.State);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.Contains("/subscriptions"));
    }

    [Fact]
    public async Task Subscribe_ReusesCustomer_WhenReferenceAlreadyExists()
    {
        var handler = new StubHandler(request => Route(request, lookupStatus: HttpStatusCode.OK, subscriptions: "[]"));
        var service = CreateService(handler);

        await service.SubscribeAsync(Shopper, "basic-plan", default);

        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.Contains("/customers"));
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.Contains("/subscriptions.json"));
    }

    [Fact]
    public async Task ListMySubscriptions_ReturnsEmpty_WhenCustomerDoesNotExist()
    {
        var handler = new StubHandler(request =>
        {
            Assert.Contains("/customers/lookup", request.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("""{"errors":["not found"]}""", Encoding.UTF8, "application/json")
            };
        });
        var service = CreateService(handler);

        var result = await service.ListMySubscriptionsAsync(Shopper, default);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ListMySubscriptions_MapsStatePriceAndNextBillingDate()
    {
        var handler = new StubHandler(request => Route(request, lookupStatus: HttpStatusCode.OK, subscriptions: """
            [
              {
                "subscription": {
                  "id": 9,
                  "state": "active",
                  "product_price_in_cents": 2900,
                  "next_assessment_at": "2026-10-01T12:00:00Z",
                  "product": { "handle": "basic-plan", "name": "Basic Plan", "price_in_cents": 2900 }
                }
              }
            ]
            """));
        var service = CreateService(handler);

        var result = await service.ListMySubscriptionsAsync(Shopper, default);

        var sub = Assert.Single(result);
        Assert.Equal("basic-plan", sub.ProductHandle);
        Assert.Equal("Basic Plan", sub.ProductName);
        Assert.Equal(29.00m, sub.Price);
        Assert.Equal("active", sub.State);
        Assert.Equal(DateTimeOffset.Parse("2026-10-01T12:00:00Z"), sub.NextBillingDate);
    }

    private static MaxioSubscriptionBillingService CreateService(StubHandler handler)
    {
        var maxioOptions = new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "example",
            ProductFamilyHandle = "eshop-subscribe",
            DefaultProductHandle = "eshop-pro",
            Environment = "US"
        };

        var sdkOptions = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials
            {
                Username = "test-key",
                Password = "x"
            }
        };
        sdkOptions.Server.Production.Us.Site = "example";

        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), sdkOptions);
        return new MaxioSubscriptionBillingService(
            client,
            Options.Create(maxioOptions),
            Substitute.For<ILogger<MaxioSubscriptionBillingService>>());
    }

    private static HttpResponseMessage Route(HttpRequestMessage request, HttpStatusCode lookupStatus, string subscriptions)
    {
        var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
        var method = request.Method;

        if (method == HttpMethod.Get && path.Contains("/customers/lookup"))
        {
            if (lookupStatus == HttpStatusCode.NotFound)
            {
                return Json(HttpStatusCode.NotFound, """{"errors":["not found"]}""");
            }

            return Json(HttpStatusCode.OK, CustomerJson());
        }

        if (method == HttpMethod.Post && path.EndsWith("/customers.json", StringComparison.Ordinal))
        {
            return Json(HttpStatusCode.Created, CustomerJson());
        }

        if (method == HttpMethod.Get && path.Contains("/customers/") && path.Contains("/subscriptions"))
        {
            return Json(HttpStatusCode.OK, subscriptions);
        }

        if (method == HttpMethod.Get && path.Contains("/subscriptions/lookup"))
        {
            return Json(HttpStatusCode.NotFound, "{}");
        }

        if (method == HttpMethod.Post && path.EndsWith("/subscriptions.json", StringComparison.Ordinal))
        {
            return Json(HttpStatusCode.Created, """
                {
                  "subscription": {
                    "id": 77,
                    "state": "active",
                    "reference": "user-1:eshop-pro",
                    "product_price_in_cents": 29900,
                    "next_assessment_at": "2026-09-21T00:00:00Z",
                    "product": {
                      "handle": "eshop-pro",
                      "name": "Pro Plan",
                      "price_in_cents": 29900,
                      "interval": 1,
                      "interval_unit": "month"
                    }
                  }
                }
                """);
        }

        return Json(HttpStatusCode.NotFound, """{"errors":["unexpected request"]}""");
    }

    private static string CustomerJson()
        => """
            {
              "customer": {
                "id": 42,
                "reference": "user-1",
                "email": "demouser@microsoft.com",
                "first_name": "demouser",
                "last_name": "User"
              }
            }
            """;

    private static HttpResponseMessage Json(HttpStatusCode status, string json)
        => new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responder(request));
        }
    }
}
