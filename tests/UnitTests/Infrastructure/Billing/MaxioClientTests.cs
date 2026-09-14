using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioClientTests
{
    [Fact]
    public async Task UsesBaseUrlOverrideBasicAuthAndOpenApiRequestShape()
    {
        Uri? requestUri = null;
        AuthenticationHeaderValue? authorization = null;
        JsonElement requestBody = default;
        var handler = new StubHandler(async request =>
        {
            requestUri = request.RequestUri;
            authorization = request.Headers.Authorization;
            requestBody = JsonDocument.Parse(await request.Content!.ReadAsStringAsync())
                .RootElement.Clone();
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(
                    """{"subscription":{"id":7,"state":"active","product_price_in_cents":29900,"currency":"USD","customer":{"id":42},"product":{"id":1,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month"}}}""",
                    Encoding.UTF8,
                    "application/json")
            };
        });
        var client = new MaxioClient(
            new HttpClient(handler),
            Options.Create(new MaxioOptions
            {
                ApiKey = "api-key",
                Subdomain = "ignored-by-override",
                ProductFamilyHandle = "family",
                BaseUrl = "https://billing.example.test/proxy"
            }));

        var result = await client.CreateSubscriptionAsync(new MaxioCreateSubscription
        {
            ProductHandle = "eshop-pro",
            CustomerId = 42,
            PaymentCollectionMethod = "remittance",
            Reference = "subscription-reference"
        }, CancellationToken.None);

        Assert.Equal("https://billing.example.test/proxy/subscriptions.json", requestUri!.ToString());
        Assert.Equal("Basic", authorization!.Scheme);
        Assert.Equal("api-key:x", Encoding.UTF8.GetString(Convert.FromBase64String(authorization.Parameter!)));
        var subscription = requestBody.GetProperty("subscription");
        Assert.Equal("eshop-pro", subscription.GetProperty("product_handle").GetString());
        Assert.Equal(42, subscription.GetProperty("customer_id").GetInt64());
        Assert.Equal("remittance", subscription.GetProperty("payment_collection_method").GetString());
        Assert.Equal("subscription-reference", subscription.GetProperty("reference").GetString());
        Assert.Equal(7, result.Id);
    }

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
