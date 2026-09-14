using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Subscriptions;

[TestClass]
public class MaxioClientContractTests
{
    [TestMethod]
    public async Task UsesSpecProductFamilyPathAndBasicAuthentication()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json("""
                [{"product":{"id":12,"name":"Pro","handle":"pro","description":"Plan","price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false,"archived_at":null,"product_family":{"handle":"family"}}}]
                """)
        });
        var client = CreateClient(handler, baseUrl: "https://billing.example.test/custom-root");

        var products = await client.ListProductsForFamilyAsync("family", CancellationToken.None);

        Assert.AreEqual("https://billing.example.test/custom-root/product_families/handle%3Afamily/products.json", handler.RequestUri);
        Assert.AreEqual("Basic", handler.AuthenticationScheme);
        Assert.AreEqual("contract-key:x", Encoding.UTF8.GetString(Convert.FromBase64String(handler.AuthenticationParameter!)));
        Assert.AreEqual(29900, products[0].PriceInCents);
        Assert.AreEqual("month", products[0].IntervalUnit);
    }

    [TestMethod]
    public async Task UsesSpecSubscriptionRequestShape()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = Json("""
                {"subscription":{"id":34,"state":"active","product_price_in_cents":2900,"current_period_ends_at":"2026-09-21T00:00:00Z","next_assessment_at":"2026-09-21T00:00:00Z","reference":"subscription-ref","customer":{"id":9,"reference":"customer-ref"},"product":{"id":2,"name":"Basic","handle":"basic","price_in_cents":2900,"interval":1,"interval_unit":"month","require_credit_card":false,"archived_at":null,"product_family":{"handle":"family"}}}}
                """)
        });
        var client = CreateClient(handler);

        var subscription = await client.CreateSubscriptionAsync(
            new CreateMaxioSubscription("basic", 9, "subscription-ref", "remittance"),
            CancellationToken.None);

        Assert.AreEqual("https://site.chargify.com/subscriptions.json", handler.RequestUri);
        Assert.AreEqual(HttpMethod.Post, handler.Method);
        StringAssert.Contains(handler.RequestBody!, "\"product_handle\":\"basic\"");
        StringAssert.Contains(handler.RequestBody!, "\"customer_id\":9");
        StringAssert.Contains(handler.RequestBody!, "\"reference\":\"subscription-ref\"");
        StringAssert.Contains(handler.RequestBody!, "\"payment_collection_method\":\"remittance\"");
        Assert.AreEqual("active", subscription.State);
        Assert.AreEqual(2900, subscription.ProductPriceInCents);
    }

    [TestMethod]
    public async Task MapsSpecErrorListFromUnprocessableEntity()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = Json("""{"errors":["Product handle is invalid."]}""")
        });
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsExceptionAsync<MaxioApiException>(() =>
            client.CreateSubscriptionAsync(
                new CreateMaxioSubscription("missing", 9, "subscription-ref", "remittance"),
                CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, exception.StatusCode);
        Assert.AreEqual("Product handle is invalid.", exception.Message);
    }

    private static MaxioClient CreateClient(RecordingHandler handler, string? baseUrl = null)
    {
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "contract-key",
            Subdomain = "site",
            ProductFamilyHandle = "family",
            BaseUrl = baseUrl
        });
        return new MaxioClient(new HttpClient(handler), options);
    }

    private static StringContent Json(string value) => new(value, Encoding.UTF8, "application/json");

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public RecordingHandler(params HttpResponseMessage[] responses)
        {
            _responses = new Queue<HttpResponseMessage>(responses);
        }

        public string? RequestUri { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string? AuthenticationScheme { get; private set; }
        public string? AuthenticationParameter { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.AbsoluteUri;
            Method = request.Method;
            AuthenticationScheme = request.Headers.Authorization?.Scheme;
            AuthenticationParameter = request.Headers.Authorization?.Parameter;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return _responses.Dequeue();
        }
    }
}
