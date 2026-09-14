using System.Net;
using System.Text;
using Microsoft.eShopWeb.PublicApi.SubscriptionBilling;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.PublicApi.SubscriptionBilling;

public class MaxioClientContractTests
{
    [Fact]
    public async Task CreateSubscriptionUsesSpecPathAndEnvelope()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        var handler = new StubHandler(async request =>
        {
            capturedRequest = request;
            capturedBody = await request.Content!.ReadAsStringAsync();
            return JsonResponse(HttpStatusCode.Created, """
                {"subscription":{"id":42,"state":"active","product_price_in_cents":29900,"currency":"USD","customer":{"id":7},"product":{"id":3,"handle":"eshop-pro","name":"Pro","price_in_cents":29900,"interval":1,"interval_unit":"month","product_family":{"id":1,"handle":"test-family"}}}}
                """);
        });
        var client = NewClient(handler);

        var result = await client.CreateSubscriptionAsync(
            new MaxioCreateSubscription
            {
                ProductHandle = "eshop-pro",
                CustomerId = 7,
                Reference = "ref-1",
                PaymentCollectionMethod = "remittance"
            },
            CancellationToken.None);

        Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
        Assert.Equal("https://maxio.test/custom/base/subscriptions.json", capturedRequest.RequestUri!.ToString());
        Assert.Contains("\"product_handle\":\"eshop-pro\"", capturedBody);
        Assert.Contains("\"customer_id\":7", capturedBody);
        Assert.Contains("\"reference\":\"ref-1\"", capturedBody);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", capturedBody);
        Assert.Equal(42, result.Id);
    }

    [Fact]
    public async Task CustomerLookupUsesSpecQueryAndTreats404AsMissing()
    {
        Uri? capturedUri = null;
        var handler = new StubHandler(request =>
        {
            capturedUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });
        var client = NewClient(handler);

        var result = await client.FindCustomerByReferenceAsync("user/reference", CancellationToken.None);

        Assert.Null(result);
        Assert.Equal("https://maxio.test/custom/base/customers/lookup.json?reference=user%2Freference", capturedUri!.ToString());
    }

    private static MaxioClient NewClient(HttpMessageHandler handler)
    {
        return new MaxioClient(
            new HttpClient(handler),
            Options.Create(new MaxioOptions
            {
                ApiKey = "test-key",
                ProductFamilyHandle = "family",
                BaseUrl = "https://maxio.test/custom/base"
            }));
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            _handler(request);
    }
}
