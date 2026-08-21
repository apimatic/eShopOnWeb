using System.Net;
using System.Text;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.PublicApi;

public class MaxioClientTests
{
    [Fact]
    public async Task ListProductsUsesSpecPathAndBasicAuthentication()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHandler(request =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, """
                [{"product":{"id":7,"name":"Pro","handle":"pro","price_in_cents":29900,"interval":1,"interval_unit":"month","archived_at":null,"require_credit_card":false,"product_family":{"id":2,"name":"Plans","handle":"family"}}}]
                """);
        });
        var client = CreateClient(handler);

        var products = await client.ListProductsAsync("family", default);

        Assert.Single(products);
        Assert.Equal("/product_families/handle%3Afamily/products.json", captured!.RequestUri!.AbsolutePath);
        Assert.Equal("Basic", captured.Headers.Authorization!.Scheme);
        Assert.Equal("test-api-key:x", Encoding.ASCII.GetString(
            Convert.FromBase64String(captured.Headers.Authorization.Parameter!)));
        Assert.Contains(captured.Headers.Accept, value => value.MediaType == "application/json");
    }

    [Fact]
    public async Task CreateSubscriptionUsesSpecEnvelopeAndCreatedStatus()
    {
        string? body = null;
        var handler = new StubHandler(async request =>
        {
            body = await request.Content!.ReadAsStringAsync();
            return Json(HttpStatusCode.Created, """
                {"subscription":{"id":42,"state":"active","product_price_in_cents":29900,"reference":"sub-ref","next_assessment_at":"2030-01-02T00:00:00Z","customer":{"id":10,"reference":"customer-ref"},"product":{"id":7,"name":"Pro","handle":"pro","price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false,"product_family":{"id":2,"name":"Plans","handle":"family"}}}}
                """);
        });
        var client = CreateClient(handler);

        var subscription = await client.CreateSubscriptionAsync(new MaxioCreateSubscription
        {
            ProductHandle = "pro",
            CustomerId = 10,
            Reference = "sub-ref"
        }, default);

        Assert.Equal(42, subscription.Id);
        Assert.Equal(
            "{\"subscription\":{\"product_handle\":\"pro\",\"payment_collection_method\":\"remittance\",\"customer_id\":10,\"reference\":\"sub-ref\"}}",
            body);
    }

    [Fact]
    public async Task LookupReturnsNullForSpecNotFoundResponse()
    {
        var client = CreateClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));

        var customer = await client.FindCustomerAsync("missing", default);

        Assert.Null(customer);
    }

    [Fact]
    public async Task ErrorResponseUsesSpecErrorList()
    {
        var client = CreateClient(new StubHandler(_ =>
            Json(HttpStatusCode.UnprocessableEntity, "{\"errors\":[\"Product cannot be blank.\"]}")));

        var exception = await Assert.ThrowsAsync<MaxioApiException>(() =>
            client.CreateSubscriptionAsync(new MaxioCreateSubscription(), default));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, exception.StatusCode);
        Assert.Equal("Product cannot be blank.", Assert.Single(exception.Errors));
    }

    private static MaxioClient CreateClient(HttpMessageHandler handler) => new(
        new HttpClient(handler),
        Options.Create(new MaxioOptions
        {
            ApiKey = "test-api-key",
            Subdomain = "unused",
            ProductFamilyHandle = "family",
            BaseUrl = "https://maxio.test"
        }));

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string content) => new(statusCode)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
            : this(request => Task.FromResult(handler(request)))
        {
        }

        public StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) => _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => _handler(request);
    }
}
