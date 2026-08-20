using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.PublicApi.Maxio;

public class MaxioClientTests
{
    [Fact]
    public async Task ListProductsUsesSpecPathPaginationAndBasicAuth()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return JsonResponse("""
                [{"product":{"id":7,"name":"Pro","handle":"pro","description":"Plan","price_in_cents":29900,"interval":1,"interval_unit":"month","archived_at":null,"require_credit_card":false,"product_family":{"id":3,"name":"Plans","handle":"family"}}}]
                """);
        });
        var client = CreateClient(handler);

        var products = await client.ListProductsAsync(CancellationToken.None);

        Assert.Single(products);
        Assert.Equal("https://maxio.example.test/root/products.json?page=1&per_page=200", capturedRequest!.RequestUri!.ToString());
        Assert.Equal("Basic", capturedRequest.Headers.Authorization!.Scheme);
        Assert.Equal("test-api-key:x", Encoding.ASCII.GetString(
            Convert.FromBase64String(capturedRequest.Headers.Authorization.Parameter!)));
    }

    [Fact]
    public async Task CreateSubscriptionUsesSpecRequestAndResponseShape()
    {
        string? requestBody = null;
        var handler = new StubHttpMessageHandler(async request =>
        {
            requestBody = await request.Content!.ReadAsStringAsync();
            return JsonResponse("""
                {"subscription":{"id":42,"state":"active","product_price_in_cents":29900,"current_period_ends_at":"2026-09-20T12:00:00Z","next_assessment_at":"2026-09-20T12:00:00Z","reference":"ref-1","currency":"USD","customer":{"id":9,"first_name":"Demo","last_name":"Customer","email":"demo@example.com","reference":"user-1"},"product":{"id":7,"name":"Pro","handle":"pro","description":"Plan","price_in_cents":29900,"interval":1,"interval_unit":"month","archived_at":null,"require_credit_card":false,"product_family":{"id":3,"name":"Plans","handle":"family"}}}}
                """);
        });
        var client = CreateClient(handler);

        var subscription = await client.CreateSubscriptionAsync(
            new MaxioCreateSubscription("pro", 9, "ref-1", "remittance"), CancellationToken.None);

        Assert.Equal(42, subscription.Id);
        Assert.Equal("active", subscription.State);
        Assert.Equal("{\"subscription\":{\"product_handle\":\"pro\",\"customer_id\":9,\"reference\":\"ref-1\",\"payment_collection_method\":\"remittance\"}}", requestBody);
    }

    private static MaxioClient CreateClient(HttpMessageHandler handler) =>
        new(new HttpClient(handler), Options.Create(new MaxioOptions
        {
            ApiKey = "test-api-key",
            Subdomain = "unused",
            ProductFamilyHandle = "family",
            BaseUrl = "https://maxio.example.test/root"
        }));

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
            : this(request => Task.FromResult(handler(request))) { }

        public StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) => _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            _handler(request);
    }
}
