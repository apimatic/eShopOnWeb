using System;
using System.Net.Http;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class MaxioBillingClientTests
{
    [TestMethod]
    public async Task UsesVerifiedFamilyEndpointAndMapsActivePlans()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """
            [
              { "product": {
                "name": "Basic Plan", "handle": "basic-plan", "description": "Basic",
                "price_in_cents": 2900, "interval": 1, "interval_unit": "month",
                "archived_at": null, "product_price_point_name": "Default",
                "product_family": { "handle": "eshop-subscribe" }
              }},
              { "product": {
                "name": "Old Plan", "handle": "old-plan", "description": "Old",
                "price_in_cents": 100, "interval": 1, "interval_unit": "month",
                "archived_at": "2026-01-01T00:00:00Z", "product_price_point_name": "Default",
                "product_family": { "handle": "eshop-subscribe" }
              }}
            ]
            """);
        var client = CreateClient(handler);

        var plans = await client.GetPlansAsync(CancellationToken.None);
        Assert.AreEqual(1, plans.Count);
        var plan = plans[0];

        Assert.AreEqual("basic-plan", plan.Handle);
        Assert.AreEqual(2900, plan.PriceInCents);
        Assert.AreEqual(HttpMethod.Get, handler.Request!.Method);
        Assert.AreEqual(
            "/product_families/handle%3Aeshop-subscribe/products.json?per_page=200&page=1",
            handler.Request.RequestUri!.PathAndQuery);
        Assert.AreEqual("Basic", handler.Request.Headers.Authorization!.Scheme);
        Assert.AreEqual(
            Convert.ToBase64String(Encoding.ASCII.GetBytes("test-key:x")),
            handler.Request.Headers.Authorization.Parameter);
    }

    [TestMethod]
    public async Task CreatesSubscriptionWithHandlesAndDeterministicReference()
    {
        var handler = new RecordingHandler(HttpStatusCode.Created, """
            { "subscription": {
              "id": 42, "state": "active", "product_price_in_cents": 29900,
              "next_assessment_at": "2026-09-21T00:00:00Z",
              "product": {
                "name": "Pro Plan", "handle": "eshop-pro", "description": "Pro",
                "price_in_cents": 29900, "interval": 1, "interval_unit": "month",
                "archived_at": null, "product_price_point_name": "Default",
                "product_family": { "handle": "eshop-subscribe" }
              }
            }}
            """);
        var client = CreateClient(handler);

        var result = await client.CreateSubscriptionAsync(
            "eshop-pro",
            "eshop-user:123",
            "eshop-subscription:123:eshop-pro",
            CancellationToken.None);

        Assert.AreEqual(42, result.Id);
        Assert.AreEqual("active", result.State);
        StringAssert.Contains(handler.RequestBody!, "\"product_handle\":\"eshop-pro\"");
        StringAssert.Contains(handler.RequestBody!, "\"customer_reference\":\"eshop-user:123\"");
        StringAssert.Contains(handler.RequestBody!, "\"reference\":\"eshop-subscription:123:eshop-pro\"");
        StringAssert.Contains(handler.RequestBody!, "\"payment_collection_method\":\"remittance\"");
    }

    private static MaxioBillingClient CreateClient(HttpMessageHandler handler) => new(
        new HttpClient(handler),
        Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "ignored",
            ProductFamilyHandle = "eshop-subscribe",
            BaseUrl = "https://billing.example.test"
        }));

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;

        public RecordingHandler(HttpStatusCode statusCode, string responseBody)
        {
            _statusCode = statusCode;
            _responseBody = responseBody;
        }

        public HttpRequestMessage? Request { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            if (request.Content is not null)
            {
                RequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
