using System.Net;
using System.Text;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioSubscriptionBillingServiceTests
{
    private const string BuyerId = "buyer-1";
    private const string ProductHandle = "eshop-pro";

    [Fact]
    public async Task ListPlansAsync_ReturnsFamilyProducts()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """
            [
              {
                "product": {
                  "id": 1,
                  "name": "Pro Plan",
                  "handle": "eshop-pro",
                  "price_in_cents": 29900,
                  "interval": 1,
                  "interval_unit": "month",
                  "require_credit_card": false
                }
              }
            ]
            """));
        var service = CreateService(handler);

        var plans = await service.ListPlansAsync(CancellationToken.None);

        Assert.Single(plans);
        Assert.Equal("eshop-pro", plans[0].Handle);
        Assert.Equal("Pro Plan", plans[0].Name);
        Assert.Equal(29900, plans[0].PriceInCents);
        Assert.Equal(299m, plans[0].Price);
        Assert.Equal("month", plans[0].IntervalUnit);
        Assert.Contains("handle%3Aeshop-subscribe", handler.LastRequest!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerAndSubscription()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.Contains("product"))
            {
                return Json(HttpStatusCode.OK, ProductListJson);
            }

            if (request.Method == HttpMethod.Get && path.Contains("customers"))
            {
                return Json(HttpStatusCode.NotFound, "{}");
            }

            if (request.Method == HttpMethod.Post && path.Contains("customers"))
            {
                return Json(HttpStatusCode.Created, """
                    { "customer": { "id": 42, "reference": "buyer-1", "email": "a@b.com", "first_name": "A", "last_name": "B" } }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.Contains("subscriptions"))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (request.Method == HttpMethod.Post && path.Contains("subscriptions"))
            {
                return Json(HttpStatusCode.Created, SubscriptionJson);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var service = CreateService(handler);

        var result = await service.SubscribeAsync(BuyerId, "a@b.com", "A", "B", ProductHandle, CancellationToken.None);

        Assert.True(result.Created);
        Assert.Equal(9, result.Subscription.Id);
        Assert.Equal("eshop-pro", result.Subscription.ProductHandle);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal(29900, result.Subscription.PriceInCents);
        Assert.NotNull(result.Subscription.NextBillingAt);
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.Contains("customers"));
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.Contains("subscriptions"));
    }

    [Fact]
    public async Task SubscribeAsync_ReturnsExistingSubscriptionWithoutCreatingAnother()
    {
        var createCalls = 0;
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.Contains("product"))
            {
                return Json(HttpStatusCode.OK, ProductListJson);
            }

            if (request.Method == HttpMethod.Get && path.Contains("customers"))
            {
                return Json(HttpStatusCode.OK, """
                    { "customer": { "id": 42, "reference": "buyer-1", "email": "a@b.com", "first_name": "A", "last_name": "B" } }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.Contains("subscriptions"))
            {
                return Json(HttpStatusCode.OK, SubscriptionJson);
            }

            if (request.Method == HttpMethod.Post)
            {
                createCalls++;
                return Json(HttpStatusCode.InternalServerError, "{}");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var service = CreateService(handler);

        var result = await service.SubscribeAsync(BuyerId, "a@b.com", "A", "B", ProductHandle, CancellationToken.None);

        Assert.False(result.Created);
        Assert.Equal(9, result.Subscription.Id);
        Assert.Equal(0, createCalls);
    }

    [Fact]
    public async Task ListSubscriptionsForBuyerAsync_ReturnsEmptyWhenCustomerMissing()
    {
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("customers/lookup"))
            {
                return Json(HttpStatusCode.NotFound, "{}");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var service = CreateService(handler);

        var subscriptions = await service.ListSubscriptionsForBuyerAsync(BuyerId, CancellationToken.None);

        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task ListPlansAsync_MapsProviderMissToBillingException()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.NotFound, "\"missing\""));
        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<BillingException>(() => service.ListPlansAsync(CancellationToken.None));
        Assert.Equal(502, ex.StatusCode);
    }

    private static MaxioSubscriptionBillingService CreateService(StubHandler handler)
    {
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.chargify.com")
        }, new MaxioAdvancedBillingClientOptions());
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "example",
            ProductFamilyHandle = "eshop-subscribe"
        });
        return new MaxioSubscriptionBillingService(client, options, NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private const string ProductListJson = """
        [
          {
            "product": {
              "id": 1,
              "name": "Pro Plan",
              "handle": "eshop-pro",
              "price_in_cents": 29900,
              "interval": 1,
              "interval_unit": "month",
              "require_credit_card": false
            }
          }
        ]
        """;

    private const string SubscriptionJson = """
        {
          "subscription": {
            "id": 9,
            "state": "active",
            "product_price_in_cents": 29900,
            "current_period_ends_at": "2026-09-19T00:00:00Z",
            "next_assessment_at": "2026-09-19T00:00:00Z",
            "reference": "buyer-1:eshop-pro",
            "product": {
              "handle": "eshop-pro",
              "name": "Pro Plan",
              "price_in_cents": 29900
            }
          }
        }
        """;

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public List<HttpRequestMessage> Requests { get; } = new();
        public HttpRequestMessage? LastRequest => Requests.Count == 0 ? null : Requests[^1];

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responder(request));
        }
    }
}
