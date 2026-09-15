using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioSubscriptionBillingServiceTests
{
    private static readonly Shopper DemoShopper = new("user-123", "demouser@microsoft.com", "demouser@microsoft.com");

    [Fact]
    public void CustomerReference_IsStableForUser()
    {
        Assert.Equal("eshop:user-123", MaxioSubscriptionBillingService.CustomerReference("user-123"));
    }

    [Fact]
    public void UniquenessToken_IsDeterministicForUserAndPlan()
    {
        var first = MaxioSubscriptionBillingService.UniquenessToken("user-123", "eshop-pro");
        var second = MaxioSubscriptionBillingService.UniquenessToken("user-123", "eshop-pro");
        var otherPlan = MaxioSubscriptionBillingService.UniquenessToken("user-123", "basic-plan");

        Assert.Equal(first, second);
        Assert.NotEqual(first, otherPlan);
    }

    [Theory]
    [InlineData("active", true)]
    [InlineData("trialing", true)]
    [InlineData("past_due", true)]
    [InlineData("canceled", false)]
    [InlineData("expired", false)]
    [InlineData("trial_ended", false)]
    public void IsLive_ClassifiesSubscriptionStates(string state, bool expected)
    {
        Assert.Equal(expected, MaxioSubscriptionBillingService.IsLive(state));
    }

    [Fact]
    public async Task ListPlansAsync_MapsFamilyProducts()
    {
        var handler = new ScriptedHandler()
            .On(HttpMethod.Get, "/product_families/", HttpStatusCode.OK, """
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
                      "name": "Basic Plan",
                      "handle": "basic-plan",
                      "price_in_cents": 2900,
                      "interval": 1,
                      "interval_unit": "month"
                    }
                  }
                ]
                """);

        var service = CreateService(handler);

        var plans = await service.ListPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Equal("basic-plan", plans[0].Handle);
        Assert.Equal(29.00m, plans[0].Price);
        Assert.Equal("eshop-pro", plans[1].Handle);
        Assert.Equal(299.00m, plans[1].Price);
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerAndSubscription()
    {
        var handler = new ScriptedHandler()
            .On(HttpMethod.Get, "/product_families/", HttpStatusCode.OK, PlanCatalogJson)
            .On(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.NotFound, "")
            .On(HttpMethod.Post, "/customers.json", HttpStatusCode.OK, """
                { "customer": { "id": 42, "email": "demouser@microsoft.com", "reference": "eshop:user-123" } }
                """)
            .On(HttpMethod.Get, "/customers/42/subscriptions.json", HttpStatusCode.OK, "[]")
            .On(HttpMethod.Get, "/subscriptions/lookup.json", HttpStatusCode.NotFound, "")
            .On(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.Created, """
                {
                  "subscription": {
                    "id": 99,
                    "state": "active",
                    "product_price_in_cents": 29900,
                    "current_period_ends_at": "2026-09-21T00:00:00Z",
                    "next_assessment_at": "2026-09-21T00:00:00Z",
                    "product": { "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900 }
                  }
                }
                """);

        var service = CreateService(handler);

        var result = await service.SubscribeAsync(DemoShopper, "eshop-pro");

        Assert.Equal(99, result.Subscription.Id);
        Assert.Equal("eshop-pro", result.Subscription.ProductHandle);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal(299.00m, result.Subscription.Price);
        Assert.True(result.Created);
        Assert.NotNull(result.Subscription.NextBillingAt);
        Assert.Contains(handler.PostedBodies, body => body.Contains("uniqueness_token"));
        Assert.Contains(handler.PostedBodies, body => body.Contains("\"reference\":\"eshop:user-123\""));
        Assert.Contains(handler.PostedBodies, body => body.Contains("\"product_handle\":\"eshop-pro\""));
        Assert.Contains(handler.PostedBodies, body => body.Contains("\"payment_collection_method\":\"remittance\""));
    }

    [Fact]
    public async Task SubscribeAsync_ReturnsExistingLiveSubscriptionWithoutCreatingAnother()
    {
        var handler = new ScriptedHandler()
            .On(HttpMethod.Get, "/product_families/", HttpStatusCode.OK, PlanCatalogJson)
            .On(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.OK, """
                { "customer": { "id": 42, "reference": "eshop:user-123" } }
                """)
            .On(HttpMethod.Get, "/customers/42/subscriptions.json", HttpStatusCode.OK, """
                [
                  {
                    "subscription": {
                      "id": 77,
                      "state": "active",
                      "product_price_in_cents": 29900,
                      "product": { "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900 }
                    }
                  }
                ]
                """);

        var service = CreateService(handler);

        var result = await service.SubscribeAsync(DemoShopper, "eshop-pro");

        Assert.Equal(77, result.Subscription.Id);
        Assert.False(result.Created);
        Assert.DoesNotContain(handler.PostedBodies, b => b.Contains("product_handle"));
    }

    [Fact]
    public async Task SubscribeAsync_ThrowsWhenPlanIsUnknown()
    {
        var handler = new ScriptedHandler()
            .On(HttpMethod.Get, "/product_families/", HttpStatusCode.OK, PlanCatalogJson);

        var service = CreateService(handler);

        await Assert.ThrowsAsync<PlanNotFoundException>(() => service.SubscribeAsync(DemoShopper, "no-such-plan"));
    }

    private const string PlanCatalogJson = """
        [
          {
            "product": {
              "id": 1,
              "name": "Pro Plan",
              "handle": "eshop-pro",
              "price_in_cents": 29900,
              "interval": 1,
              "interval_unit": "month"
            }
          }
        ]
        """;

    private static MaxioSubscriptionBillingService CreateService(ScriptedHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new System.Uri("https://example.chargify.com/")
        };
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "example",
            ProductFamilyHandle = "eshop-subscribe"
        });
        var api = new MaxioApiClient(httpClient, options, NullLogger<MaxioApiClient>.Instance);
        var cache = new MemoryCache(new MemoryCacheOptions());
        return new MaxioSubscriptionBillingService(api, options, cache, NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly System.Collections.Generic.List<(HttpMethod Method, string PathContains, HttpStatusCode Status, string Body)> _scripts = new();

        public System.Collections.Generic.List<string> Paths { get; } = new();
        public System.Collections.Generic.List<string> PostedBodies { get; } = new();

        public ScriptedHandler On(HttpMethod method, string pathContains, HttpStatusCode status, string body)
        {
            _scripts.Add((method, pathContains, status, body));
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.PathAndQuery ?? string.Empty;
            Paths.Add(path);
            if (request.Content is not null)
            {
                PostedBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }

            foreach (var script in _scripts)
            {
                if (request.Method == script.Method && path.Contains(script.PathContains, System.StringComparison.OrdinalIgnoreCase))
                {
                    return new HttpResponseMessage(script.Status)
                    {
                        Content = new StringContent(script.Body, Encoding.UTF8, "application/json")
                    };
                }
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent($"{{\"errors\":[\"No script for {request.Method} {path}\"]}}", Encoding.UTF8, "application/json")
            };
        }
    }
}
