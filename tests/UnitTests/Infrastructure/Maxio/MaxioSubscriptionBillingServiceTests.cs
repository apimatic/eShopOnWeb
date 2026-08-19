using System.Net;
using System.Text;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Entities.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionBillingServiceTests
{
    private static readonly ShopperIdentity Shopper = new("user-123", "demo@example.com", "demo.user@example.com");

    [Fact]
    public void SplitName_UsesEmailLocalPart()
    {
        var (first, last) = MaxioSubscriptionBillingService.SplitName(Shopper);

        Assert.Equal("Demo", first);
        Assert.Equal("User", last);
    }

    [Fact]
    public void BuildSubscriptionReference_IsStablePerUserAndPlan()
    {
        Assert.Equal("user-123:eshop-pro", MaxioSubscriptionBillingService.BuildSubscriptionReference("user-123", "eshop-pro"));
    }

    [Fact]
    public async Task ListPlansAsync_MapsProductsFromConfiguredFamily()
    {
        var handler = new ScriptedHandler()
            .On(HttpMethod.Get, "/product_families/handle%3Aeshop-subscribe/products.json", HttpStatusCode.OK,
                """
                [
                  {
                    "product": {
                      "id": 1,
                      "handle": "eshop-pro",
                      "name": "Pro Plan",
                      "description": "Full access",
                      "price_in_cents": 29900,
                      "interval": 1,
                      "interval_unit": "month"
                    }
                  },
                  {
                    "product": {
                      "id": 2,
                      "handle": "archived-plan",
                      "name": "Old",
                      "price_in_cents": 100,
                      "interval": 1,
                      "interval_unit": "month",
                      "archived_at": "2020-01-01T00:00:00Z"
                    }
                  }
                ]
                """);

        var service = CreateService(handler);
        var plans = await service.ListPlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.Equal("month", plan.IntervalUnit);
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerAndSubscription()
    {
        var handler = new ScriptedHandler()
            .On(HttpMethod.Get, "/product_families/handle%3Aeshop-subscribe/products.json", HttpStatusCode.OK,
                """[{"product":{"id":1,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month"}}]""")
            .On(HttpMethod.Get, "/customers/lookup.json?reference=user-123", HttpStatusCode.NotFound, "")
            .On(HttpMethod.Post, "/customers.json", HttpStatusCode.OK,
                """{"customer":{"id":77,"email":"demo@example.com","reference":"user-123"}}""")
            .On(HttpMethod.Get, "/subscriptions/lookup.json?reference=user-123%3Aeshop-pro", HttpStatusCode.NotFound, "")
            .On(HttpMethod.Get, "/customers/77/subscriptions.json", HttpStatusCode.OK, "[]")
            .On(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.Created,
                """
                {
                  "subscription": {
                    "id": 9001,
                    "state": "active",
                    "product_price_in_cents": 29900,
                    "next_assessment_at": "2026-09-19T12:00:00Z",
                    "product": { "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900 }
                  }
                }
                """);

        var service = CreateService(handler);
        var result = await service.SubscribeAsync(Shopper, "eshop-pro");

        Assert.Equal(9001, result.Id);
        Assert.Equal("active", result.State);
        Assert.Equal("eshop-pro", result.ProductHandle);
        Assert.Equal(29900, result.PriceInCents);
        Assert.Equal(new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero), result.NextBillingDate);
        Assert.Equal(1, handler.Count(HttpMethod.Post, "/customers.json"));
        Assert.Equal(1, handler.Count(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task SubscribeAsync_IsIdempotentWhenLiveSubscriptionExists()
    {
        var handler = new ScriptedHandler()
            .On(HttpMethod.Get, "/product_families/handle%3Aeshop-subscribe/products.json", HttpStatusCode.OK,
                """[{"product":{"id":1,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month"}}]""")
            .On(HttpMethod.Get, "/customers/lookup.json?reference=user-123", HttpStatusCode.OK,
                """{"customer":{"id":77,"email":"demo@example.com","reference":"user-123"}}""")
            .On(HttpMethod.Get, "/subscriptions/lookup.json?reference=user-123%3Aeshop-pro", HttpStatusCode.OK,
                """
                {
                  "subscription": {
                    "id": 9001,
                    "state": "active",
                    "product_price_in_cents": 29900,
                    "next_assessment_at": "2026-09-19T12:00:00Z",
                    "product": { "handle": "eshop-pro", "name": "Pro Plan" }
                  }
                }
                """);

        var service = CreateService(handler);
        var result = await service.SubscribeAsync(Shopper, "eshop-pro");

        Assert.Equal(9001, result.Id);
        Assert.Equal(0, handler.Count(HttpMethod.Post, "/subscriptions.json"));
        Assert.Equal(0, handler.Count(HttpMethod.Post, "/customers.json"));
    }

    [Fact]
    public async Task SubscribeAsync_ThrowsWhenPlanIsUnknown()
    {
        var handler = new ScriptedHandler()
            .On(HttpMethod.Get, "/product_families/handle%3Aeshop-subscribe/products.json", HttpStatusCode.OK,
                """[{"product":{"id":1,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month"}}]""");

        var service = CreateService(handler);
        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(() => service.SubscribeAsync(Shopper, "no-such-plan"));
    }

    [Fact]
    public async Task ListSubscriptionsAsync_ReturnsEmptyWhenCustomerDoesNotExist()
    {
        var handler = new ScriptedHandler()
            .On(HttpMethod.Get, "/customers/lookup.json?reference=user-123", HttpStatusCode.NotFound, "");

        var service = CreateService(handler);
        var result = await service.ListSubscriptionsAsync(Shopper);

        Assert.Empty(result);
    }

    private static MaxioSubscriptionBillingService CreateService(ScriptedHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.chargify.com/")
        };
        var options = Options.Create(new MaxioSettings { ProductFamilyHandle = "eshop-subscribe" });
        return new MaxioSubscriptionBillingService(httpClient, options, NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly List<(HttpMethod Method, string PathAndQuery, HttpStatusCode Status, string Body)> _scripts = new();
        private readonly List<(HttpMethod Method, string PathAndQuery)> _calls = new();

        public ScriptedHandler On(HttpMethod method, string pathAndQuery, HttpStatusCode status, string body)
        {
            _scripts.Add((method, pathAndQuery, status, body));
            return this;
        }

        public int Count(HttpMethod method, string pathAndQuery)
            => _calls.Count(call => call.Method == method && call.PathAndQuery.StartsWith(pathAndQuery, StringComparison.Ordinal));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            _calls.Add((request.Method, path));

            var match = _scripts.FirstOrDefault(script =>
                script.Method == request.Method &&
                path.StartsWith(script.PathAndQuery.Split('?')[0], StringComparison.Ordinal) &&
                (!script.PathAndQuery.Contains('?') ||
                 path.Equals(script.PathAndQuery, StringComparison.Ordinal) ||
                 path.StartsWith(script.PathAndQuery, StringComparison.Ordinal)));

            if (match == default)
            {
                throw new InvalidOperationException($"Unexpected request {request.Method} {path}");
            }

            var response = new HttpResponseMessage(match.Status)
            {
                Content = new StringContent(match.Body, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
