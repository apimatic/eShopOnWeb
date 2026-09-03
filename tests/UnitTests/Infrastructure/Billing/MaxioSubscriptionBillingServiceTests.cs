using System.Net;
using System.Text;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioSubscriptionBillingServiceTests
{
    private const string FamilyHandle = "eshop-subscribe";
    private static readonly ShopperIdentity Shopper = new("user-1", "demouser@microsoft.com", "demouser", "Shopper");

    [Fact]
    public void CustomerReferenceIsStablePerUser()
    {
        Assert.Equal("eshop-user:user-1", MaxioSubscriptionBillingService.CustomerReferenceFor("user-1"));
        Assert.Equal(
            "eshop-sub:user-1:eshop-pro",
            MaxioSubscriptionBillingService.SubscriptionReferenceFor("user-1", "eshop-pro"));
    }

    [Fact]
    public async Task ListPlansAsync_MapsFamilyProducts()
    {
        var handler = new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Contains("product_families", request.RequestUri!.AbsolutePath, StringComparison.OrdinalIgnoreCase);
            var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
            Assert.Contains("handle:eshop-subscribe", path, StringComparison.OrdinalIgnoreCase);
            return Json(HttpStatusCode.OK, ProductsJson());
        });

        var service = CreateService(handler);

        var plans = await service.ListPlansAsync(CancellationToken.None);

        Assert.Equal(2, plans.Count);
        Assert.Contains(plans, p => p.Handle == "eshop-pro" && p.PriceInCents == 29900 && p.IntervalUnit == "month");
        Assert.Contains(plans, p => p.Handle == "basic-plan" && p.PriceInCents == 2900);
    }

    [Fact]
    public async Task EnrollAsync_CreatesCustomerAndSubscription()
    {
        var posts = 0;
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            var query = request.RequestUri!.Query;

            if (request.Method == HttpMethod.Get && path.Contains("products", StringComparison.OrdinalIgnoreCase))
            {
                return Json(HttpStatusCode.OK, ProductsJson());
            }

            if (request.Method == HttpMethod.Get && path.Contains("customers/lookup", StringComparison.OrdinalIgnoreCase))
            {
                return Json(HttpStatusCode.NotFound, "{}");
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/customers.json", StringComparison.OrdinalIgnoreCase))
            {
                posts++;
                return Json(HttpStatusCode.Created, CustomerJson());
            }

            if (request.Method == HttpMethod.Get && path.Contains("subscriptions/lookup", StringComparison.OrdinalIgnoreCase))
            {
                return Json(HttpStatusCode.NotFound, string.Empty);
            }

            if (request.Method == HttpMethod.Get && path.Contains("/subscriptions.json", StringComparison.OrdinalIgnoreCase)
                && path.Contains("/customers/", StringComparison.OrdinalIgnoreCase))
            {
                return Json(HttpStatusCode.OK, "[]");
            }

            if (request.Method == HttpMethod.Get && path.Contains("site", StringComparison.OrdinalIgnoreCase))
            {
                return Json(HttpStatusCode.OK, """{ "site": { "relationship_invoicing_enabled": true } }""");
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json", StringComparison.OrdinalIgnoreCase))
            {
                posts++;
                var sent = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                Assert.Contains("\"payment_collection_method\":\"remittance\"", sent);
                return Json(HttpStatusCode.Created, SubscriptionJson());
            }

            throw new InvalidOperationException($"Unexpected request {request.Method} {path}{query}");
        });

        var service = CreateService(handler);
        var result = await service.EnrollAsync(Shopper, "eshop-pro", CancellationToken.None);

        Assert.True(result.Created);
        Assert.Equal(99, result.Subscription.Id);
        Assert.Equal("eshop-pro", result.Subscription.ProductHandle);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal(29900, result.Subscription.ProductPriceInCents);
        Assert.NotNull(result.Subscription.NextBillingDate);
        Assert.Equal(2, posts);
    }

    [Fact]
    public async Task EnrollAsync_IsIdempotentWhenSubscriptionAlreadyExists()
    {
        var createSubscriptionCalls = 0;
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;

            if (request.Method == HttpMethod.Get && path.Contains("products", StringComparison.OrdinalIgnoreCase))
            {
                return Json(HttpStatusCode.OK, ProductsJson());
            }

            if (request.Method == HttpMethod.Get && path.Contains("customers/lookup", StringComparison.OrdinalIgnoreCase))
            {
                return Json(HttpStatusCode.OK, CustomerJson());
            }

            if (request.Method == HttpMethod.Get && path.Contains("subscriptions/lookup", StringComparison.OrdinalIgnoreCase))
            {
                return Json(HttpStatusCode.OK, SubscriptionJson());
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json", StringComparison.OrdinalIgnoreCase))
            {
                createSubscriptionCalls++;
                return Json(HttpStatusCode.Created, SubscriptionJson());
            }

            throw new InvalidOperationException($"Unexpected request {request.Method} {path}");
        });

        var service = CreateService(handler);
        var result = await service.EnrollAsync(Shopper, "eshop-pro", CancellationToken.None);

        Assert.False(result.Created);
        Assert.Equal(99, result.Subscription.Id);
        Assert.Equal(0, createSubscriptionCalls);
    }

    [Fact]
    public async Task ListMySubscriptionsAsync_ReturnsEmptyWhenCustomerMissing()
    {
        var handler = new StubHandler(request =>
        {
            Assert.Contains("customers/lookup", request.RequestUri!.AbsolutePath, StringComparison.OrdinalIgnoreCase);
            return Json(HttpStatusCode.NotFound, "{}");
        });

        var service = CreateService(handler);
        var result = await service.ListMySubscriptionsAsync(Shopper, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task EnrollAsync_RejectsUnknownProductHandle()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, ProductsJson()));
        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<BillingProviderException>(
            () => service.EnrollAsync(Shopper, "not-a-plan", CancellationToken.None));

        Assert.Equal(400, ex.StatusCode);
    }

    private static MaxioSubscriptionBillingService CreateService(StubHandler handler)
    {
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(10)
        }, new MaxioAdvancedBillingClientOptions
        {
            Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(5), MaxRetries = 1 }
        });

        return new MaxioSubscriptionBillingService(
            client,
            Options.Create(new MaxioOptions { ProductFamilyHandle = FamilyHandle }),
            NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static string ProductsJson() => """
        [
          {
            "product": {
              "id": 1,
              "handle": "eshop-pro",
              "name": "Pro Plan",
              "price_in_cents": 29900,
              "interval": 1,
              "interval_unit": "month",
              "require_credit_card": false,
              "product_family": { "handle": "eshop-subscribe", "name": "eShop Subscribe" }
            }
          },
          {
            "product": {
              "id": 2,
              "handle": "basic-plan",
              "name": "Basic Plan",
              "price_in_cents": 2900,
              "interval": 1,
              "interval_unit": "month",
              "require_credit_card": false,
              "product_family": { "handle": "eshop-subscribe", "name": "eShop Subscribe" }
            }
          }
        ]
        """;

    private static string CustomerJson() => """
        {
          "customer": {
            "id": 42,
            "reference": "eshop-user:user-1",
            "email": "demouser@microsoft.com",
            "first_name": "demouser",
            "last_name": "Shopper"
          }
        }
        """;

    private static string SubscriptionJson() => """
        {
          "subscription": {
            "id": 99,
            "reference": "eshop-sub:user-1:eshop-pro",
            "state": "active",
            "product_price_in_cents": 29900,
            "current_billing_amount_in_cents": 29900,
            "next_assessment_at": "2026-10-01T00:00:00Z",
            "current_period_ends_at": "2026-10-01T00:00:00Z",
            "current_period_started_at": "2026-09-01T00:00:00Z",
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

public sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public List<HttpRequestMessage> Requests { get; } = new();

    public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(_responder(request));
    }
}
