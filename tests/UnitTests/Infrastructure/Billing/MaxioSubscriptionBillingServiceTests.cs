using System.Net;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioSubscriptionBillingServiceTests
{
    private const string FamilyHandle = "eshop-subscribe";
    private const string ProHandle = "eshop-pro";
    private const string BuyerId = "buyer-123";

    [Fact]
    public async Task ListPlans_ReturnsMappedCatalog()
    {
        var service = CreateService((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
            Assert.Contains("product_families", path);
            Assert.Contains($"handle:{FamilyHandle}", path);

            return Json(HttpStatusCode.OK, """
                [
                  {
                    "product": {
                      "id": 7126957,
                      "name": "Pro Plan",
                      "handle": "eshop-pro",
                      "price_in_cents": 29900,
                      "interval": 1,
                      "interval_unit": "month",
                      "require_credit_card": false,
                      "product_family": { "id": 1, "name": "eShop", "handle": "eshop-subscribe" }
                    }
                  },
                  {
                    "product": {
                      "id": 7126958,
                      "name": "Basic Plan",
                      "handle": "basic-plan",
                      "price_in_cents": 2900,
                      "interval": 1,
                      "interval_unit": "month",
                      "require_credit_card": false,
                      "product_family": { "id": 1, "name": "eShop", "handle": "eshop-subscribe" }
                    }
                  }
                ]
                """);
        });

        var plans = await service.ListPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Equal(ProHandle, plans[0].Handle);
        Assert.Equal(299.00m, plans[0].Price);
        Assert.Equal("month", plans[0].IntervalUnit);
        Assert.False(plans[0].RequiresCreditCard);
        Assert.Equal("basic-plan", plans[1].Handle);
        Assert.Equal(29.00m, plans[1].Price);
    }

    [Fact]
    public async Task Subscribe_CreatesCustomerAndSubscription_WhenBuyerIsNew()
    {
        var posts = 0;
        var service = CreateService((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.Contains("products") && path.Contains(ProHandle))
            {
                return ProductJson();
            }

            if (request.Method == HttpMethod.Get && path.Contains("customers") && request.RequestUri.Query.Contains("reference"))
            {
                return Json(HttpStatusCode.NotFound, """{ "errors": "Not Found" }""");
            }

            if (request.Method == HttpMethod.Post && path.Contains("customers"))
            {
                posts++;
                return Json(HttpStatusCode.Created, """
                    { "customer": { "id": 10, "reference": "buyer-123", "email": "demouser@microsoft.com", "first_name": "demouser", "last_name": "eShop" } }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.Contains("customers/10/subscriptions"))
            {
                return Json(HttpStatusCode.OK, "[]");
            }

            if (request.Method == HttpMethod.Post && path.Contains("subscriptions"))
            {
                posts++;
                return SubscriptionJson();
            }

            return Json(HttpStatusCode.NotFound, "{}");
        });

        var result = await service.SubscribeAsync(BuyerId, "demouser@microsoft.com", "demouser@microsoft.com", ProHandle);

        Assert.Equal(99, result.Id);
        Assert.Equal(ProHandle, result.ProductHandle);
        Assert.Equal("Pro Plan", result.ProductName);
        Assert.Equal(299.00m, result.Price);
        Assert.Equal("active", result.State);
        Assert.NotNull(result.NextBillingAt);
        Assert.Equal(2, posts);
    }

    [Fact]
    public async Task Subscribe_ReturnsExistingOpenSubscription_WithoutCreatingAnother()
    {
        var createCalls = 0;
        var service = CreateService((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.Contains("products") && path.Contains(ProHandle))
            {
                return ProductJson();
            }

            if (request.Method == HttpMethod.Get && path.Contains("customers") && request.RequestUri.Query.Contains("reference"))
            {
                return Json(HttpStatusCode.OK, """
                    { "customer": { "id": 10, "reference": "buyer-123", "email": "demouser@microsoft.com", "first_name": "demouser", "last_name": "eShop" } }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.Contains("subscriptions"))
            {
                return Json(HttpStatusCode.OK, $"""
                    [ {SubscriptionJsonBody()} ]
                    """);
            }

            if (request.Method == HttpMethod.Post && path.Contains("subscriptions"))
            {
                createCalls++;
                return SubscriptionJson();
            }

            return Json(HttpStatusCode.NotFound, "{}");
        });

        var result = await service.SubscribeAsync(BuyerId, "demouser@microsoft.com", null, ProHandle);

        Assert.Equal(99, result.Id);
        Assert.Equal(0, createCalls);
    }

    [Fact]
    public async Task ListSubscriptions_ReturnsEmpty_WhenCustomerDoesNotExist()
    {
        var service = CreateService((_, _) => Json(HttpStatusCode.NotFound, """{ "errors": "Not Found" }"""));

        var result = await service.ListSubscriptionsAsync(BuyerId);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Subscribe_MapsCreateSubscription422ToBillingException()
    {
        var service = CreateService((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.Contains("products"))
            {
                return ProductJson();
            }

            if (request.Method == HttpMethod.Get && request.RequestUri.Query.Contains("reference"))
            {
                return Json(HttpStatusCode.OK, """
                    { "customer": { "id": 10, "reference": "buyer-123", "email": "a@b.com", "first_name": "A", "last_name": "B" } }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.Contains("subscriptions"))
            {
                return Json(HttpStatusCode.OK, "[]");
            }

            if (request.Method == HttpMethod.Post)
            {
                return Json(HttpStatusCode.UnprocessableEntity, """{ "errors": ["Product cannot be found"] }""");
            }

            return Json(HttpStatusCode.NotFound, "{}");
        });

        var ex = await Assert.ThrowsAsync<BillingException>(() =>
            service.SubscribeAsync(BuyerId, "a@b.com", null, ProHandle));

        Assert.Equal(422, ex.StatusCode);
        Assert.Equal("Product cannot be found", ex.Message);
    }

    private static ISubscriptionBillingService CreateService(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
    {
        var handler = new StubHandler(responder);
        var httpClient = new HttpClient(handler);
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "example",
            ProductFamilyHandle = FamilyHandle
        });
        var client = MaxioBillingClientFactory.Create(httpClient, options.Value);
        return new MaxioSubscriptionBillingService(client, options, new NullLogger());
    }

    private static HttpResponseMessage ProductJson() => Json(HttpStatusCode.OK, """
        {
          "product": {
            "id": 7126957,
            "name": "Pro Plan",
            "handle": "eshop-pro",
            "price_in_cents": 29900,
            "interval": 1,
            "interval_unit": "month",
            "require_credit_card": false,
            "product_family": { "id": 1, "name": "eShop", "handle": "eshop-subscribe" }
          }
        }
        """);

    private static HttpResponseMessage SubscriptionJson() =>
        Json(HttpStatusCode.Created, SubscriptionJsonBody());

    private static string SubscriptionJsonBody() => """
        {
          "subscription": {
            "id": 99,
            "state": "active",
            "product_price_in_cents": 29900,
            "current_period_ends_at": "2026-09-20T00:00:00Z",
            "next_assessment_at": "2026-09-20T00:00:00Z",
            "reference": "buyer-123:eshop-pro",
            "customer": { "id": 10, "reference": "buyer-123", "email": "demouser@microsoft.com" },
            "product": {
              "id": 7126957,
              "name": "Pro Plan",
              "handle": "eshop-pro",
              "price_in_cents": 29900,
              "interval": 1,
              "interval_unit": "month"
            }
          }
        }
        """;

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _responder;
        public List<HttpRequestMessage> Requests { get; } = new();

        public StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responder(request, cancellationToken));
        }
    }

    private sealed class NullLogger : IAppLogger<MaxioSubscriptionBillingService>
    {
        public void LogInformation(string message, params object[] args) { }
        public void LogWarning(string message, params object[] args) { }
    }
}
