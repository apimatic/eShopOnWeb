using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioClientTests
{
    [Fact]
    public async Task CreateSubscriptionUsesVerifiedContractAndBasicAuthentication()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        var handler = new RecordingHandler(async request =>
        {
            capturedRequest = request;
            capturedBody = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(
                    """
                    {"subscription":{"id":42,"reference":"subscription-ref","state":"active","product_price_in_cents":2900,"next_assessment_at":"2026-09-21T00:00:00Z","currency":"USD","customer":{"id":7,"reference":"customer-ref","email":"shopper@example.com"},"product":{"id":9,"handle":"basic-plan","name":"Basic Plan","price_in_cents":2900,"interval":1,"interval_unit":"month"}}}
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        });
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-api-key",
            Subdomain = "test-site",
            ProductFamilyHandle = "test-family"
        });
        var client = new MaxioClient(new HttpClient(handler), options);

        var result = await client.CreateSubscriptionAsync(
            new MaxioCreateSubscription(
                "basic-plan",
                "customer-ref",
                "subscription-ref",
                "52f45142-a633-4d8f-9f48-5e963867eb36"),
            CancellationToken.None);

        Assert.Equal(42, result.Id);
        Assert.Equal("https://test-site.chargify.com/subscriptions.json", capturedRequest!.RequestUri!.ToString());
        Assert.Equal("Basic", capturedRequest.Headers.Authorization!.Scheme);
        Assert.Equal(
            "test-api-key:X",
            Encoding.ASCII.GetString(Convert.FromBase64String(capturedRequest.Headers.Authorization.Parameter!)));

        using var body = JsonDocument.Parse(capturedBody!);
        Assert.Equal("basic-plan", body.RootElement.GetProperty("subscription").GetProperty("product_handle").GetString());
        Assert.Equal("customer-ref", body.RootElement.GetProperty("subscription").GetProperty("customer_reference").GetString());
        Assert.Equal("subscription-ref", body.RootElement.GetProperty("subscription").GetProperty("reference").GetString());
        Assert.Equal(
            "remittance",
            body.RootElement.GetProperty("subscription").GetProperty("payment_collection_method").GetString());
        Assert.Equal(
            "52f45142-a633-4d8f-9f48-5e963867eb36",
            body.RootElement.GetProperty("uniqueness_token").GetString());
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        internal RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => _handler(request);
    }
}
