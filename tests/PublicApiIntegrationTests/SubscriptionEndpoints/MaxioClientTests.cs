using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints.Maxio;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class MaxioClientTests
{
    [TestMethod]
    public async Task ListsPlansByConfiguredFamilyHandleAndUsesBasicAuth()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHandler(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    [{"product":{"id":42,"handle":"pro","name":"Pro","description":"Plan","price_in_cents":29900,"interval":1,"interval_unit":"month","archived_at":null,"product_price_point_name":"Default"}}]
                    """, Encoding.UTF8, "application/json")
            };
        });
        var client = CreateClient(handler);

        var products = await client.ListProductsAsync(CancellationToken.None);

        Assert.AreEqual(1, products.Count);
        Assert.AreEqual(29900, products[0].PriceInCents);
        Assert.AreEqual("https://billing.test/root/product_families/handle%3Afamily/products.json", captured!.RequestUri!.AbsoluteUri);
        Assert.AreEqual("Basic", captured.Headers.Authorization!.Scheme);
        Assert.AreEqual("test-key:x", Encoding.ASCII.GetString(Convert.FromBase64String(captured.Headers.Authorization.Parameter!)));
    }

    [TestMethod]
    public async Task CreatesRemittanceSubscriptionUsingStableHandlesAndReferences()
    {
        string? requestBody = null;
        var handler = new StubHandler(async request =>
        {
            requestBody = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("""
                    {"subscription":{"id":9,"state":"active","reference":"sub-ref","product_price_in_cents":2900,"next_assessment_at":"2026-09-21T00:00:00Z","customer":{"id":7,"reference":"customer-ref","email":"shopper@example.com"},"product":{"id":4,"handle":"basic-plan","name":"Basic Plan","interval":1,"interval_unit":"month","product_price_point_name":"Default"}}}
                    """, Encoding.UTF8, "application/json")
            };
        });
        var client = CreateClient(handler);

        var subscription = await client.CreateSubscriptionAsync(
            new CreateMaxioSubscription("basic-plan", "customer-ref", "sub-ref"),
            CancellationToken.None);

        Assert.AreEqual(9, subscription.Id);
        StringAssert.Contains(requestBody!, "\"product_handle\":\"basic-plan\"");
        StringAssert.Contains(requestBody!, "\"customer_reference\":\"customer-ref\"");
        StringAssert.Contains(requestBody!, "\"reference\":\"sub-ref\"");
        StringAssert.Contains(requestBody!, "\"payment_collection_method\":\"remittance\"");
    }

    private static MaxioClient CreateClient(HttpMessageHandler handler)
    {
        return new MaxioClient(
            new HttpClient(handler),
            Options.Create(new MaxioOptions
            {
                ApiKey = "test-key",
                Subdomain = "unused",
                ProductFamilyHandle = "family",
                BaseUrl = "https://billing.test/root"
            }));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
            : this(request => Task.FromResult(handler(request))) { }

        public StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            _handler(request);
    }
}
