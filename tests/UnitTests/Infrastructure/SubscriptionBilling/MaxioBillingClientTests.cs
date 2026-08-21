using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.Infrastructure.SubscriptionBilling;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.SubscriptionBilling;

public class MaxioBillingClientTests
{
    [Fact]
    public async Task UsesBaseUrlBasicAuthenticationAndSnakeCaseRequests()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        var handler = new StubHandler(async request =>
        {
            capturedRequest = request;
            capturedBody = request.Content is null ? null : await request.Content.ReadAsStringAsync();
            return JsonResponse(HttpStatusCode.Created, SubscriptionJson);
        });
        var client = CreateClient(handler, "https://example.test/custom/api");

        var subscription = await client.CreateSubscriptionAsync(
            "pro-plan",
            123,
            "subscription-reference",
            "duplicate-prevention-token",
            "remittance");

        Assert.Equal(42, subscription.Id);
        Assert.Equal("https://example.test/custom/api/subscriptions.json", capturedRequest!.RequestUri!.AbsoluteUri);
        Assert.Equal("Basic", capturedRequest.Headers.Authorization!.Scheme);
        Assert.Equal(
            "unit-test-key:X",
            Encoding.ASCII.GetString(Convert.FromBase64String(capturedRequest.Headers.Authorization.Parameter!)));
        Assert.Contains("\"product_handle\":\"pro-plan\"", capturedBody);
        Assert.Contains("\"customer_id\":123", capturedBody);
        Assert.Contains("\"uniqueness_token\":\"duplicate-prevention-token\"", capturedBody);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", capturedBody);
    }

    [Fact]
    public async Task UsesHandleBasedProductFamilyLookup()
    {
        Uri? capturedUri = null;
        var handler = new StubHandler(request =>
        {
            capturedUri = request.RequestUri;
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, "[]"));
        });
        var client = CreateClient(handler);

        var products = await client.GetProductsAsync("family-handle");

        Assert.Empty(products);
        Assert.Equal(
            "/product_families/handle%3Afamily-handle/products.json?per_page=200",
            capturedUri!.PathAndQuery);
    }

    private static MaxioBillingClient CreateClient(HttpMessageHandler handler, string? baseUrl = null)
    {
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "unit-test-key",
            Subdomain = "unit-test-site",
            ProductFamilyHandle = "family-handle",
            BaseUrl = baseUrl
        });
        return new MaxioBillingClient(new HttpClient(handler), options);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) => new(statusCode)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private const string SubscriptionJson = """
        {
          "subscription": {
            "id": 42,
            "state": "active",
            "product_price_in_cents": 29900,
            "current_period_ends_at": "2026-09-21T00:00:00Z",
            "next_assessment_at": "2026-09-21T00:00:00Z",
            "customer": { "id": 7, "email": "shopper@example.com", "reference": "customer-reference" },
            "product": {
              "id": 8,
              "name": "Pro",
              "handle": "pro-plan",
              "description": "Pro plan",
              "price_in_cents": 29900,
              "interval": 1,
              "interval_unit": "month",
              "require_credit_card": false,
              "archived_at": null,
              "product_family": { "id": 9, "name": "Plans", "handle": "family-handle" }
            }
          }
        }
        """;

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => _handler(request);
    }
}
