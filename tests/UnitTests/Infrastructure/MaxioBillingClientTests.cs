using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure;

public class MaxioBillingClientTests
{
    [Fact]
    public async Task CreateSubscriptionUsesDocumentedHandleCustomerAndReferencePayload()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        var handler = new StubHandler(async request =>
        {
            if (request.RequestUri!.AbsolutePath == "/site.json")
            {
                return JsonResponse("{\"site\":{\"relationship_invoicing_enabled\":true}}");
            }

            capturedRequest = request;
            capturedBody = await request.Content!.ReadAsStringAsync();
            return JsonResponse("""
                {"subscription":{"id":99,"reference":"ref-1","state":"active","product_price_in_cents":29900,
                "current_period_ends_at":"2026-09-20T00:00:00Z","next_assessment_at":"2026-09-20T00:00:00Z","currency":"USD",
                "product":{"name":"Pro","handle":"pro","description":null,"price_in_cents":29900,"interval":1,
                "interval_unit":"month","require_credit_card":false,"product_family":{"handle":"family"}}}}
                """);
        });
        var client = CreateClient(handler);

        var result = await client.CreateSubscriptionAsync(42, "pro", "ref-1", CancellationToken.None);

        Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
        Assert.Equal("https://example.test/subscriptions.json", capturedRequest.RequestUri!.ToString());
        Assert.Contains("\"product_handle\":\"pro\"", capturedBody);
        Assert.Contains("\"customer_id\":42", capturedBody);
        Assert.Contains("\"reference\":\"ref-1\"", capturedBody);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", capturedBody);
        Assert.Equal(29900, result.PriceInCents);
        Assert.Equal("active", result.State);
    }

    [Fact]
    public async Task ListPlansScopesCatalogByConfiguredFamilyHandle()
    {
        Uri? capturedUri = null;
        var handler = new StubHandler(request =>
        {
            capturedUri = request.RequestUri;
            return Task.FromResult(JsonResponse("""
                [{"product":{"name":"Pro","handle":"pro","description":"Plan","price_in_cents":29900,"interval":1,
                "interval_unit":"month","require_credit_card":false,"product_family":{"handle":"family"}}}]
                """));
        });
        var client = CreateClient(handler);

        var plans = await client.ListPlansAsync(CancellationToken.None);

        Assert.Equal("https://example.test/product_families/handle:family/products.json?per_page=200", capturedUri!.ToString());
        Assert.Single(plans);
        Assert.Equal("pro", plans[0].Handle);
    }

    [Fact]
    public async Task ListSubscriptionsReturnsOnlyConfiguredFamily()
    {
        var handler = new StubHandler(_ => Task.FromResult(JsonResponse("""
            [{"subscription":{"id":1,"state":"active","product_price_in_cents":29900,
              "product":{"name":"Pro","handle":"pro","price_in_cents":29900,"interval":1,"interval_unit":"month",
              "require_credit_card":false,"product_family":{"handle":"family"}}}},
             {"subscription":{"id":2,"state":"active","product_price_in_cents":100,
              "product":{"name":"Other","handle":"other","price_in_cents":100,"interval":1,"interval_unit":"month",
              "require_credit_card":false,"product_family":{"handle":"another-family"}}}}]
            """)));
        var client = CreateClient(handler);

        var subscriptions = await client.ListSubscriptionsAsync(42, CancellationToken.None);

        var subscription = Assert.Single(subscriptions);
        Assert.Equal(1, subscription.Id);
        Assert.Equal("pro", subscription.ProductHandle);
    }

    private static MaxioBillingClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        return new MaxioBillingClient(httpClient, Options.Create(new MaxioOptions
        {
            ApiKey = "not-a-real-key",
            Subdomain = "example",
            ProductFamilyHandle = "family"
        }));
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;
        public StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) => _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            _handler(request);
    }
}
