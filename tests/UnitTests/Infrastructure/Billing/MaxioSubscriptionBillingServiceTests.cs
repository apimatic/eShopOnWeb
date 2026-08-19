using System.Net;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioSubscriptionBillingServiceTests
{
    private const string FamilyHandle = "eshop-subscribe";
    private const string ProHandle = "eshop-pro";

    [Fact]
    public void FamilyPathId_PrefixesHandleAndEncodesColon()
    {
        Assert.Equal("handle%3Aeshop-subscribe", MaxioSubscriptionBillingService.FamilyPathId(FamilyHandle));
        Assert.Equal("handle%3Aeshop-subscribe", MaxioSubscriptionBillingService.FamilyPathId("handle:eshop-subscribe"));
    }

    [Fact]
    public void NamesFromShopper_UsesEmailLocalPart()
    {
        var shopper = new ShopperIdentity("user-1", "demouser@microsoft.com", "demouser@microsoft.com");
        var (first, last) = MaxioSubscriptionBillingService.NamesFromShopper(shopper);
        Assert.Equal("Demouser", first);
        Assert.Equal("Customer", last);
    }

    [Fact]
    public void CentsToAmount_ConvertsToMajorCurrencyUnits()
    {
        Assert.Equal(299.00m, MaxioSubscriptionBillingService.CentsToAmount(29900));
        Assert.Equal(29.00m, MaxioSubscriptionBillingService.CentsToAmount(2900));
    }

    [Fact]
    public async Task ListPlansAsync_ReturnsActiveProductsInTheConfiguredFamily()
    {
        var handler = new ScriptedHandler();
        handler.OnGet($"product_families/{MaxioSubscriptionBillingService.FamilyPathId(FamilyHandle)}/products.json",
            """
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
                  "name": "Retired",
                  "handle": "retired",
                  "description": "",
                  "price_in_cents": 100,
                  "interval": 1,
                  "interval_unit": "month",
                  "archived_at": "2024-01-01T00:00:00Z"
                }
              }
            ]
            """);

        var service = CreateService(handler);
        var plans = await service.ListPlansAsync();

        Assert.Single(plans);
        Assert.Equal(ProHandle, plans[0].Handle);
        Assert.Equal(299.00m, plans[0].Price);
        Assert.Equal("month", plans[0].IntervalUnit);
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerThenSubscription()
    {
        var handler = new ScriptedHandler();
        handler.OnGet($"products/handle/{ProHandle}.json", ProductJson());
        handler.OnGet("customers/lookup.json", HttpStatusCode.NotFound);
        handler.OnPost("customers.json",
            """
            { "customer": { "id": 42, "email": "demouser@microsoft.com", "reference": "eshoponweb:user-1" } }
            """);
        handler.OnGet("subscriptions/lookup.json", HttpStatusCode.NotFound);
        handler.OnPost("subscriptions.json", SubscriptionJson());

        var service = CreateService(handler);
        var result = await service.SubscribeAsync(DemoShopper(), ProHandle);

        Assert.Equal(99, result.Id);
        Assert.Equal(ProHandle, result.ProductHandle);
        Assert.Equal("active", result.State);
        Assert.Equal(299.00m, result.Price);
        Assert.NotNull(result.NextBillingDate);
        Assert.Contains(handler.PostedPaths, p => p.Contains("customers.json"));
        Assert.Contains(handler.PostedPaths, p => p.Contains("subscriptions.json"));
    }

    [Fact]
    public async Task SubscribeAsync_IsIdempotentWhenSubscriptionAlreadyExists()
    {
        var handler = new ScriptedHandler();
        handler.OnGet($"products/handle/{ProHandle}.json", ProductJson());
        handler.OnGet("customers/lookup.json",
            """
            { "customer": { "id": 42, "email": "demouser@microsoft.com", "reference": "eshoponweb:user-1" } }
            """);
        handler.OnGet("subscriptions/lookup.json", SubscriptionJson());

        var service = CreateService(handler);
        var result = await service.SubscribeAsync(DemoShopper(), ProHandle);

        Assert.Equal(99, result.Id);
        Assert.Empty(handler.PostedPaths);
    }

    [Fact]
    public async Task ListShopperSubscriptionsAsync_ReturnsEmptyWhenCustomerDoesNotExist()
    {
        var handler = new ScriptedHandler();
        handler.OnGet("customers/lookup.json", HttpStatusCode.NotFound);

        var service = CreateService(handler);
        var result = await service.ListShopperSubscriptionsAsync(DemoShopper());

        Assert.Empty(result);
    }

    [Fact]
    public async Task SubscribeAsync_RejectsPlanOutsideTheConfiguredFamily()
    {
        var handler = new ScriptedHandler();
        handler.OnGet($"products/handle/{ProHandle}.json",
            """
            {
              "product": {
                "id": 1,
                "name": "Pro Plan",
                "handle": "eshop-pro",
                "price_in_cents": 29900,
                "interval": 1,
                "interval_unit": "month",
                "product_family": { "handle": "some-other-family" }
              }
            }
            """);

        var service = CreateService(handler);
        var ex = await Assert.ThrowsAsync<BillingException>(() => service.SubscribeAsync(DemoShopper(), ProHandle));
        Assert.Equal(400, ex.StatusCode);
    }

    private static ShopperIdentity DemoShopper() =>
        new("user-1", "demouser@microsoft.com", "demouser@microsoft.com");

    private static MaxioSubscriptionBillingService CreateService(ScriptedHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var settings = Options.Create(new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "example",
            ProductFamilyHandle = FamilyHandle
        });
        return new MaxioSubscriptionBillingService(httpClient, settings, NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    private static string ProductJson() =>
        """
        {
          "product": {
            "id": 1,
            "name": "Pro Plan",
            "handle": "eshop-pro",
            "description": "Pro",
            "price_in_cents": 29900,
            "interval": 1,
            "interval_unit": "month",
            "product_family": { "handle": "eshop-subscribe" }
          }
        }
        """;

    private static string SubscriptionJson() =>
        """
        {
          "subscription": {
            "id": 99,
            "state": "active",
            "product_price_in_cents": 29900,
            "next_assessment_at": "2026-09-19T00:00:00Z",
            "product": { "id": 1, "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900 }
          }
        }
        """;

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly List<(string Match, HttpStatusCode Status, string Body)> _gets = new();
        private readonly List<(string Match, HttpStatusCode Status, string Body)> _posts = new();

        public List<string> PostedPaths { get; } = new();

        public void OnGet(string urlContains, string json, HttpStatusCode status = HttpStatusCode.OK)
            => _gets.Add((urlContains, status, json));

        public void OnGet(string urlContains, HttpStatusCode status)
            => _gets.Add((urlContains, status, string.Empty));

        public void OnPost(string urlContains, string json, HttpStatusCode status = HttpStatusCode.Created)
            => _posts.Add((urlContains, status, json));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? string.Empty;
            var scripts = request.Method == HttpMethod.Post ? _posts : _gets;
            if (request.Method == HttpMethod.Post)
            {
                PostedPaths.Add(url);
            }

            var match = scripts.FirstOrDefault(s => url.Contains(s.Match, StringComparison.OrdinalIgnoreCase));
            if (match.Match is null)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(match.Status)
            {
                Content = new StringContent(match.Body, Encoding.UTF8, "application/json")
            });
        }
    }
}
