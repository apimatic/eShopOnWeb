using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class MaxioClientTests
{
    [TestMethod]
    public async Task ListProductsUsesSpecPathAndBasicAuthentication()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHandler(request =>
        {
            captured = request;
            return Json(HttpStatusCode.OK,
                """[{"product":{"id":7,"name":"Pro","handle":"pro","price_in_cents":29900,"interval":1,"interval_unit":"month","archived_at":null,"require_credit_card":false}}]""");
        });
        var client = CreateClient(handler);

        var products = await client.ListProductsAsync(CancellationToken.None);

        Assert.AreEqual(1, products.Count);
        Assert.AreEqual(29900, products[0].PriceInCents);
        Assert.AreEqual("/product_families/handle%3Aplans/products.json", captured!.RequestUri!.AbsolutePath);
        Assert.AreEqual("Basic", captured.Headers.Authorization!.Scheme);
        Assert.AreEqual("test-key:x", Encoding.UTF8.GetString(
            Convert.FromBase64String(captured.Headers.Authorization.Parameter!)));
    }

    [TestMethod]
    public async Task CreateSubscriptionUsesOnlyFieldsDefinedBySpec()
    {
        string? body = null;
        var handler = new StubHandler(async request =>
        {
            body = await request.Content!.ReadAsStringAsync();
            return Json(HttpStatusCode.Created,
                """{"subscription":{"id":42,"state":"active","product_price_in_cents":29900,"current_period_ends_at":"2026-09-21T00:00:00Z","next_assessment_at":"2026-09-21T00:00:00Z","reference":"sub-ref","currency":"USD","customer":{"id":5,"email":"shopper@example.com","reference":"customer-ref"},"product":{"id":7,"name":"Pro","handle":"pro","price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false}}}""");
        });
        var client = CreateClient(handler);

        var subscription = await client.CreateSubscriptionAsync(5, "pro", "sub-ref", CancellationToken.None);

        using var document = JsonDocument.Parse(body!);
        var payload = document.RootElement.GetProperty("subscription");
        CollectionAssert.AreEquivalent(new[]
            {
                "product_handle", "customer_id", "reference", "payment_collection_method"
            },
            payload.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.AreEqual("remittance", payload.GetProperty("payment_collection_method").GetString());
        Assert.AreEqual(42, subscription.Id);
        Assert.AreEqual("active", subscription.State);
    }

    [TestMethod]
    public async Task LookupReturnsNullForSpecNotFoundResponse()
    {
        var client = CreateClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));

        var customer = await client.FindCustomerAsync("missing", CancellationToken.None);
        var subscription = await client.FindSubscriptionAsync("missing", CancellationToken.None);

        Assert.IsNull(customer);
        Assert.IsNull(subscription);
    }

    private static MaxioClient CreateClient(HttpMessageHandler handler)
    {
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = "plans",
            BaseUrl = "https://billing.test/"
        });
        return new MaxioClient(new HttpClient(handler), options);
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string json) => new(statusCode)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
            : this(request => Task.FromResult(handler(request))) { }

        public StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => _handler(request);
    }
}
