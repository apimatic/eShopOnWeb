using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Billing;

public class MaxioOptionsTests
{
    [Fact]
    public void ResolveApiBaseUrl_UsesChargifyHostFromSubdomain()
    {
        var options = new MaxioOptions { Subdomain = "cp-exp-2" };

        Assert.Equal("https://cp-exp-2.chargify.com", options.ResolveApiBaseUrl("US"));
    }

    [Fact]
    public void ResolveApiBaseUrl_UsesEuHostWhenRegionIsEu()
    {
        var options = new MaxioOptions { Subdomain = "acme" };

        Assert.Equal("https://acme.ebilling.maxio.com", options.ResolveApiBaseUrl("EU"));
    }

    [Fact]
    public void ResolveApiBaseUrl_UsesBaseUrlVerbatimWhenSet()
    {
        var options = new MaxioOptions
        {
            Subdomain = "ignored",
            BaseUrl = "https://billing.example.test/v1"
        };

        Assert.Equal("https://billing.example.test/v1", options.ResolveApiBaseUrl("EU"));
    }

    [Fact]
    public void ToHttpClientBaseAddress_AppendsTrailingSlash()
    {
        var uri = MaxioBillingService.ToHttpClientBaseAddress("https://cp-exp-2.chargify.com");

        Assert.Equal("https://cp-exp-2.chargify.com/", uri.ToString());
    }
}

public class MaxioBillingServiceTests
{
    private static readonly MaxioOptions ConfiguredOptions = new()
    {
        ApiKey = "test-key",
        Subdomain = "example",
        ProductFamilyHandle = "eshop-subscribe"
    };

    [Fact]
    public async Task ListPlansAsync_MapsPriceInCentsToDecimal()
    {
        var handler = new ScriptedHandler();
        handler.Enqueue(HttpMethod.Get, "/product_families/handle:eshop-subscribe/products.json", HttpStatusCode.OK, """
            [
              {
                "product": {
                  "id": 1,
                  "name": "Pro Plan",
                  "handle": "eshop-pro",
                  "description": "Monthly pro",
                  "price_in_cents": 29900,
                  "interval": 1,
                  "interval_unit": "month",
                  "product_family": { "id": 9, "handle": "eshop-subscribe", "name": "eShop" }
                }
              },
              {
                "product": {
                  "id": 2,
                  "name": "Basic Plan",
                  "handle": "basic-plan",
                  "description": "Monthly basic",
                  "price_in_cents": 2900,
                  "interval": 1,
                  "interval_unit": "month",
                  "product_family": { "id": 9, "handle": "eshop-subscribe", "name": "eShop" }
                }
              }
            ]
            """);

        var service = CreateService(handler);

        var plans = await service.ListPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Equal("eshop-pro", plans[0].Handle);
        Assert.Equal(299.00m, plans[0].Price);
        Assert.Equal("basic-plan", plans[1].Handle);
        Assert.Equal(29.00m, plans[1].Price);
        Assert.Equal(0, handler.Remaining);
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerThenSubscription()
    {
        var handler = new ScriptedHandler();
        EnqueuePlanList(handler);
        handler.Enqueue(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.NotFound, "");
        handler.Enqueue(HttpMethod.Post, "/customers.json", HttpStatusCode.OK, """
            { "customer": { "id": 42, "email": "demouser@microsoft.com", "reference": "user-1", "first_name": "Demouser", "last_name": "eShopOnWeb" } }
            """);
        handler.Enqueue(HttpMethod.Get, "/customers/42/subscriptions.json", HttpStatusCode.OK, "[]");
        handler.Enqueue(HttpMethod.Get, "/subscriptions/lookup.json", HttpStatusCode.NotFound, "");
        handler.Enqueue(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.Created, """
            {
              "subscription": {
                "id": 1001,
                "state": "active",
                "product_price_in_cents": 29900,
                "next_assessment_at": "2026-09-20T00:00:00-04:00",
                "current_period_ends_at": "2026-09-20T00:00:00-04:00",
                "reference": "user-1:eshop-pro",
                "product": { "id": 1, "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900, "interval": 1, "interval_unit": "month" }
              }
            }
            """);

        var service = CreateService(handler);

        var result = await service.SubscribeAsync(new SubscribeToPlanRequest
        {
            CustomerReference = "user-1",
            Email = "demouser@microsoft.com",
            FirstName = "Demouser",
            LastName = "eShopOnWeb",
            ProductHandle = "eshop-pro"
        });

        Assert.Equal(1001, result.Id);
        Assert.Equal("active", result.State);
        Assert.Equal("eshop-pro", result.ProductHandle);
        Assert.Equal("Pro Plan", result.ProductName);
        Assert.Equal(299.00m, result.Price);
        Assert.NotNull(result.NextBillingAt);
        Assert.Equal(0, handler.Remaining);
        Assert.Contains(handler.Sent, r => r.Method == HttpMethod.Post && r.Path.Contains("/subscriptions.json"));
    }

    [Fact]
    public async Task SubscribeAsync_ReturnsExistingLiveSubscriptionWithoutCreatingAnother()
    {
        var handler = new ScriptedHandler();
        EnqueuePlanList(handler);
        handler.Enqueue(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.OK, """
            { "customer": { "id": 42, "email": "demouser@microsoft.com", "reference": "user-1" } }
            """);
        handler.Enqueue(HttpMethod.Get, "/customers/42/subscriptions.json", HttpStatusCode.OK, """
            [
              {
                "subscription": {
                  "id": 1001,
                  "state": "active",
                  "product_price_in_cents": 29900,
                  "next_assessment_at": "2026-09-20T00:00:00-04:00",
                  "current_period_ends_at": "2026-09-20T00:00:00-04:00",
                  "reference": "user-1:eshop-pro",
                  "product": { "id": 1, "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900 }
                }
              }
            ]
            """);

        var service = CreateService(handler);

        var result = await service.SubscribeAsync(new SubscribeToPlanRequest
        {
            CustomerReference = "user-1",
            Email = "demouser@microsoft.com",
            FirstName = "Demouser",
            LastName = "eShopOnWeb",
            ProductHandle = "eshop-pro"
        });

        Assert.Equal(1001, result.Id);
        Assert.DoesNotContain(handler.Sent, r => r.Method == HttpMethod.Post && r.Path.Contains("/subscriptions.json"));
        Assert.DoesNotContain(handler.Sent, r => r.Method == HttpMethod.Post && r.Path.Contains("/customers.json"));
    }

    [Fact]
    public async Task GetSubscriptionsForCustomerAsync_ReturnsEmptyWhenCustomerDoesNotExist()
    {
        var handler = new ScriptedHandler();
        handler.Enqueue(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.NotFound, "");

        var service = CreateService(handler);

        var result = await service.GetSubscriptionsForCustomerAsync("user-1");

        Assert.Empty(result);
    }

    [Fact]
    public async Task SubscribeAsync_RejectsPlanOutsideConfiguredFamily()
    {
        var handler = new ScriptedHandler();
        EnqueuePlanList(handler);

        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<BillingException>(() => service.SubscribeAsync(new SubscribeToPlanRequest
        {
            CustomerReference = "user-1",
            Email = "demouser@microsoft.com",
            FirstName = "Demouser",
            LastName = "eShopOnWeb",
            ProductHandle = "not-a-plan"
        }));

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("Unknown subscription plan", ex.Message);
    }

    private static MaxioBillingService CreateService(ScriptedHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.chargify.com/")
        };

        return new MaxioBillingService(
            httpClient,
            Options.Create(ConfiguredOptions),
            NullLogger<MaxioBillingService>.Instance);
    }

    private static void EnqueuePlanList(ScriptedHandler handler)
    {
        handler.Enqueue(HttpMethod.Get, "/product_families/handle:eshop-subscribe/products.json", HttpStatusCode.OK, """
            [
              {
                "product": {
                  "id": 1,
                  "name": "Pro Plan",
                  "handle": "eshop-pro",
                  "price_in_cents": 29900,
                  "interval": 1,
                  "interval_unit": "month",
                  "product_family": { "id": 9, "handle": "eshop-subscribe", "name": "eShop" }
                }
              }
            ]
            """);
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<ScriptedResponse> _responses = new();
        public List<(HttpMethod Method, string Path, string? Body)> Sent { get; } = new();
        public int Remaining => _responses.Count;

        public void Enqueue(HttpMethod method, string pathContains, HttpStatusCode statusCode, string body)
        {
            _responses.Enqueue(new ScriptedResponse(method, pathContains, statusCode, body));
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.PathAndQuery ?? string.Empty;
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Sent.Add((request.Method, path, body));

            if (_responses.Count == 0)
            {
                throw new InvalidOperationException($"Unexpected request {request.Method} {path}");
            }

            var next = _responses.Dequeue();
            if (request.Method != next.Method || !path.Contains(next.PathContains, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Expected {next.Method} path containing '{next.PathContains}' but received {request.Method} {path}");
            }

            return new HttpResponseMessage(next.StatusCode)
            {
                Content = new StringContent(next.Body, Encoding.UTF8, "application/json")
            };
        }

        private sealed record ScriptedResponse(HttpMethod Method, string PathContains, HttpStatusCode StatusCode, string Body);
    }
}
