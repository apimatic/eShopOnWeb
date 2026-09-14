using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioBillingGatewayTests
{
    [Fact]
    public async Task ListPlansUsesConfiguredFamilyAndBasicAuthentication()
    {
        var handler = new DelegateHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https://maxio.test/api/products.json?page=1&per_page=200", request.RequestUri!.ToString());
            AssertBasicAuthentication(request.Headers.Authorization);
            return JsonResponse(HttpStatusCode.OK, """
                [
                  { "product": { "handle": "pro", "name": "Pro", "description": "Pro plan", "price_in_cents": 29900, "interval": 1, "interval_unit": "month", "archived_at": null, "product_family": { "handle": "eshop" } } },
                  { "product": { "handle": "other", "name": "Other", "description": "Other plan", "price_in_cents": 100, "interval": 1, "interval_unit": "month", "archived_at": null, "product_family": { "handle": "another-family" } } }
                ]
                """);
        });
        var gateway = CreateGateway(handler);

        var plans = await gateway.ListPlansAsync(CancellationToken.None);

        var plan = Assert.Single(plans);
        Assert.Equal("pro", plan.Handle);
        Assert.Equal(29900, plan.PriceInCents);
    }

    [Fact]
    public async Task CreateSubscriptionSendsVerifiedShapeAndMapsBillingState()
    {
        var handler = new AsyncDelegateHandler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://maxio.test/api/subscriptions.json", request.RequestUri!.ToString());
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var subscription = document.RootElement.GetProperty("subscription");
            Assert.Equal("pro", subscription.GetProperty("product_handle").GetString());
            Assert.Equal(41, subscription.GetProperty("customer_id").GetInt64());
            Assert.Equal("reference", subscription.GetProperty("reference").GetString());
            Assert.Equal("remittance", subscription.GetProperty("payment_collection_method").GetString());
            return JsonResponse(HttpStatusCode.Created, """
                {
                  "subscription": {
                    "id": 82,
                    "state": "active",
                    "product_price_in_cents": 29900,
                    "current_period_ends_at": "2026-09-21T12:00:00Z",
                    "created_at": "2026-08-21T12:00:00Z",
                    "customer": { "id": 41, "reference": "customer-reference" },
                    "product": { "handle": "pro", "name": "Pro", "price_in_cents": 29900, "interval": 1, "interval_unit": "month", "archived_at": null, "product_family": { "handle": "eshop" } }
                  }
                }
                """);
        });
        var gateway = CreateGateway(handler);

        var result = await gateway.CreateSubscriptionAsync(41, "pro", "reference", CancellationToken.None);

        Assert.Equal(82, result.Id);
        Assert.Equal("active", result.State);
        Assert.Equal(29900, result.PriceInCents);
        Assert.Equal(DateTimeOffset.Parse("2026-09-21T12:00:00Z"), result.NextBillingAt);
    }

    private static MaxioBillingGateway CreateGateway(HttpMessageHandler handler) => new(
        new HttpClient(handler),
        Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "unused",
            ProductFamilyHandle = "eshop",
            BaseUrl = "https://maxio.test/api"
        }));

    private static void AssertBasicAuthentication(AuthenticationHeaderValue? authorization)
    {
        Assert.NotNull(authorization);
        Assert.Equal("Basic", authorization.Scheme);
        Assert.Equal("test-key:X", Encoding.ASCII.GetString(Convert.FromBase64String(authorization.Parameter!)));
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) => new(statusCode)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class DelegateHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(_handler(request));
    }

    private sealed class AsyncDelegateHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public AsyncDelegateHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) => _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => _handler(request);
    }
}
