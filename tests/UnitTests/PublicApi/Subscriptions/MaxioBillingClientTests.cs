using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.PublicApi.Subscriptions;

public class MaxioBillingClientTests
{
    [Fact]
    public async Task ListProductsUsesBaseUrlFamilyHandleAndBasicAuthentication()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHandler(request =>
        {
            captured = request;
            return Json("[{\"product\":{\"id\":1,\"name\":\"Pro\",\"handle\":\"pro\",\"price_in_cents\":29900,\"interval\":1,\"interval_unit\":\"month\",\"require_credit_card\":false,\"product_family\":{\"handle\":\"family\"}}}]");
        });
        var client = CreateClient(handler);

        var products = await client.ListProductsAsync(CancellationToken.None);

        Assert.Single(products);
        Assert.Equal("https://maxio.test/root/product_families/handle:family/products.json?page=1&per_page=200", captured!.RequestUri!.ToString());
        Assert.Equal("Basic", captured.Headers.Authorization!.Scheme);
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("api-key:X")), captured.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task CreateSubscriptionSendsDocumentedEnvelopeAndUniquenessToken()
    {
        string? requestBody = null;
        var handler = new StubHandler(async request =>
        {
            requestBody = await request.Content!.ReadAsStringAsync();
            return Json("{\"subscription\":{\"id\":9,\"state\":\"active\",\"product_price_in_cents\":2900,\"product\":{\"id\":1,\"name\":\"Basic\",\"handle\":\"basic\",\"price_in_cents\":2900,\"interval\":1,\"interval_unit\":\"month\",\"require_credit_card\":false,\"product_family\":{\"handle\":\"family\"}}}}", HttpStatusCode.Created);
        });
        var client = CreateClient(handler);

        await client.CreateSubscriptionAsync(new CreateMaxioSubscription
        {
            ProductHandle = "basic",
            CustomerId = 7,
            Reference = "sub-ref",
            UniquenessToken = "token"
        }, CancellationToken.None);

        Assert.Contains("\"subscription\"", requestBody);
        Assert.Contains("\"product_handle\":\"basic\"", requestBody);
        Assert.Contains("\"customer_id\":7", requestBody);
        Assert.Contains("\"reference\":\"sub-ref\"", requestBody);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", requestBody);
        Assert.Contains("\"uniqueness_token\":\"token\"", requestBody);
    }

    private static MaxioBillingClient CreateClient(HttpMessageHandler handler) => new(
        new HttpClient(handler),
        Options.Create(new MaxioOptions
        {
            ApiKey = "api-key",
            Subdomain = "unused",
            ProductFamilyHandle = "family",
            BaseUrl = "https://maxio.test/root"
        }),
        NullLogger<MaxioBillingClient>.Instance);

    private static HttpResponseMessage Json(string body, HttpStatusCode statusCode = HttpStatusCode.OK) => new(statusCode)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
            : this(request => Task.FromResult(handler(request)))
        {
        }

        public StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            _handler(request);
    }
}
