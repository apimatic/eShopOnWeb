using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioBillingClientTests
{
    [Fact]
    public async Task ListsActiveProductsUsingConfiguredFamilyHandle()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """
            [
              { "product": { "id": 2, "handle": "eshop-pro", "name": "Pro", "description": "Pro plan", "price_in_cents": 29900, "interval": 1, "interval_unit": "month", "require_credit_card": false, "archived_at": null } },
              { "product": { "id": 3, "handle": "old", "name": "Old", "price_in_cents": 100, "interval": 1, "interval_unit": "month", "require_credit_card": false, "archived_at": "2025-01-01T00:00:00Z" } }
            ]
            """));
        var client = CreateClient(handler);

        var plans = await client.GetPlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.Equal("https://example.test/product_families/handle%3Afamily-under-test/products.json", handler.RequestUri!.AbsoluteUri);
        Assert.Equal(HttpMethod.Get, handler.Method);
    }

    [Fact]
    public async Task CreatesSubscriptionWithSpecRequestAndMapsConfirmation()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Created, """
            {
              "subscription": {
                "id": 42,
                "reference": "eshop-sub-user-eshop-pro",
                "state": "active",
                "product_price_in_cents": 29900,
                "current_period_ends_at": "2026-09-21T12:00:00Z",
                "customer": { "id": 7, "reference": "eshop-user-user", "email": "user@example.com" },
                "product": { "id": 2, "handle": "eshop-pro", "name": "Pro", "price_in_cents": 29900, "interval": 1, "interval_unit": "month", "product_family": { "handle": "family-under-test" } }
              }
            }
            """));
        var client = CreateClient(handler);

        var subscription = await client.CreateSubscriptionAsync(7, "eshop-pro", "eshop-sub-user-eshop-pro");

        Assert.Equal(42, subscription.Id);
        Assert.Equal("active", subscription.State);
        Assert.Equal(29900, subscription.PriceInCents);
        Assert.Equal(DateTimeOffset.Parse("2026-09-21T12:00:00Z"), subscription.NextBillingAt);
        using var body = JsonDocument.Parse(handler.Body!);
        var request = body.RootElement.GetProperty("subscription");
        Assert.Equal("eshop-pro", request.GetProperty("product_handle").GetString());
        Assert.Equal(7, request.GetProperty("customer_id").GetInt64());
        Assert.Equal("eshop-sub-user-eshop-pro", request.GetProperty("reference").GetString());
        Assert.Equal("remittance", request.GetProperty("payment_collection_method").GetString());
        Assert.Equal("https://example.test/subscriptions.json", handler.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task ReturnsNullOnlyForDocumentedLookupNotFound()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.NotFound, "{}"));
        var client = CreateClient(handler);

        var subscription = await client.FindSubscriptionAsync("missing/reference");

        Assert.Null(subscription);
        Assert.Equal("?reference=missing%2Freference", handler.RequestUri!.Query);
    }

    private static MaxioBillingClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        return new MaxioBillingClient(httpClient, Options.Create(new MaxioOptions
        {
            ApiKey = "not-a-secret",
            Subdomain = "example",
            ProductFamilyHandle = "family-under-test"
        }));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _response;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) => _response = response;

        public Uri? RequestUri { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Method = request.Method;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return _response(request);
        }
    }
}
