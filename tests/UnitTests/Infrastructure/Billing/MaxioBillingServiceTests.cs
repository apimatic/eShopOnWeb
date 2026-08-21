using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioBillingServiceTests
{
    private static readonly ShopperIdentity DemoShopper = new()
    {
        UserId = "user-1",
        Email = "demouser@microsoft.com",
        FirstName = "demouser",
        LastName = "eShopOnWeb"
    };

    [Fact]
    public async Task ListPlansAsync_MapsPriceFromCents()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """
            [
              {
                "product": {
                  "handle": "eshop-pro",
                  "name": "Pro Plan",
                  "description": "Pro",
                  "price_in_cents": 29900,
                  "interval": 1,
                  "interval_unit": "month",
                  "product_price_point_handle": "eshop-pro"
                }
              }
            ]
            """));
        var service = CreateService(handler);

        var plans = await service.ListPlansAsync(default);

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal("Pro Plan", plan.Name);
        Assert.Equal(299.00m, plan.Price);
        Assert.Equal(1, plan.Interval);
        Assert.Equal("month", plan.IntervalUnit);
        Assert.Contains("product_families", handler.LastRequest!.RequestUri!.AbsolutePath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("eshop-subscribe", Uri.UnescapeDataString(handler.LastRequest.RequestUri.AbsolutePath), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SubscribeAsync_ReturnsExistingSubscriptionWithoutCreating()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            var url = request.RequestUri!.ToString();
            if (url.Contains("product", StringComparison.OrdinalIgnoreCase)
                && url.Contains("eshop-pro", StringComparison.OrdinalIgnoreCase)
                && request.Method == HttpMethod.Get
                && !url.Contains("subscription", StringComparison.OrdinalIgnoreCase))
            {
                return Json(HttpStatusCode.OK, """
                    { "product": { "handle": "eshop-pro", "name": "Pro Plan", "product_family": { "handle": "eshop-subscribe" } } }
                    """);
            }

            if (path.Contains("/customers/lookup", StringComparison.OrdinalIgnoreCase)
                || (path.Contains("/customers", StringComparison.OrdinalIgnoreCase) && url.Contains("reference", StringComparison.OrdinalIgnoreCase)))
            {
                return Json(HttpStatusCode.OK, """
                    { "customer": { "id": 42, "reference": "user-1", "email": "demouser@microsoft.com" } }
                    """);
            }

            if (path.Contains("/subscriptions/lookup", StringComparison.OrdinalIgnoreCase)
                || (path.Contains("/subscriptions", StringComparison.OrdinalIgnoreCase) && url.Contains("reference", StringComparison.OrdinalIgnoreCase)))
            {
                return Json(HttpStatusCode.OK, """
                    {
                      "subscription": {
                        "id": 99,
                        "reference": "user-1:eshop-pro",
                        "state": "active",
                        "product_price_in_cents": 29900,
                        "currency": "USD",
                        "next_assessment_at": "2026-09-21T00:00:00Z",
                        "product": { "handle": "eshop-pro", "name": "Pro Plan", "interval": 1, "interval_unit": "month" }
                      }
                    }
                    """);
            }

            return Json(HttpStatusCode.NotFound, $"unexpected {request.Method} {url}");
        });
        var service = CreateService(handler);

        var subscription = await service.SubscribeAsync(DemoShopper, "eshop-pro", default);

        Assert.Equal(99, subscription.Id);
        Assert.Equal("eshop-pro", subscription.ProductHandle);
        Assert.Equal(299.00m, subscription.Price);
        Assert.Equal("active", subscription.State);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task ListMySubscriptionsAsync_ReturnsEmptyWhenCustomerMissing()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.NotFound, "Not Found"));
        var service = CreateService(handler);

        var subscriptions = await service.ListMySubscriptionsAsync("user-1", default);

        Assert.Empty(subscriptions);
    }

    private static MaxioBillingService CreateService(StubHandler handler)
    {
        var options = new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "cp-exp-1",
            ProductFamilyHandle = "eshop-subscribe"
        };
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), MaxioServiceCollectionExtensions.CreateClientOptions(options));
        return new MaxioBillingService(client, Options.Create(options), NullLogger<MaxioBillingService>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

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
