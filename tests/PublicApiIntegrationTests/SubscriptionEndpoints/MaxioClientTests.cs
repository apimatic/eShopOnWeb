using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class MaxioClientTests
{
    [TestMethod]
    public async Task ListsProductsForConfiguredFamilyHandleUsingBasicAuthentication()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json("[{\"product\":{\"id\":7,\"name\":\"Pro\",\"handle\":\"pro\",\"price_in_cents\":29900,\"interval\":1,\"interval_unit\":\"month\",\"require_credit_card\":false}}]")
        });
        var client = CreateClient(handler);

        var products = await client.ListProductsAsync("family-one", CancellationToken.None);

        Assert.AreEqual(1, products.Count);
        Assert.AreEqual("pro", products[0].Handle);
        StringAssert.Contains(handler.RequestUri!.AbsoluteUri, "product_families/handle%3Afamily-one/products.json");
        Assert.AreEqual("Basic", handler.AuthorizationScheme);
        Assert.AreEqual(Convert.ToBase64String(Encoding.UTF8.GetBytes("secret:X")), handler.AuthorizationParameter);
    }

    [TestMethod]
    public async Task CreatesSubscriptionWithHandlesReferencesAndTopLevelUniquenessToken()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = Json("{\"subscription\":{\"id\":42,\"state\":\"active\",\"product_price_in_cents\":29900,\"product\":{\"name\":\"Pro\",\"handle\":\"pro\",\"interval\":1,\"interval_unit\":\"month\"}}}")
        });
        var client = CreateClient(handler);

        var result = await client.CreateSubscriptionAsync(
            "customer-ref",
            "pro",
            "subscription-ref",
            "remittance",
            "unique-token",
            CancellationToken.None);

        Assert.AreEqual(42L, result.Id);
        using var body = JsonDocument.Parse(handler.RequestBody!);
        Assert.AreEqual("unique-token", body.RootElement.GetProperty("uniqueness_token").GetString());
        var subscription = body.RootElement.GetProperty("subscription");
        Assert.AreEqual("customer-ref", subscription.GetProperty("customer_reference").GetString());
        Assert.AreEqual("pro", subscription.GetProperty("product_handle").GetString());
        Assert.AreEqual("subscription-ref", subscription.GetProperty("reference").GetString());
        Assert.AreEqual("remittance", subscription.GetProperty("payment_collection_method").GetString());
    }

    private static MaxioClient CreateClient(HttpMessageHandler handler) => new(
        new HttpClient(handler),
        Options.Create(new MaxioOptions
        {
            ApiKey = "secret",
            Subdomain = "site",
            ProductFamilyHandle = "family-one",
            BaseUrl = "https://billing.example.test"
        }));

    private static StringContent Json(string value) => new(value, Encoding.UTF8, "application/json");

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _response;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> response)
        {
            _response = response;
        }

        public Uri? RequestUri { get; private set; }
        public string? RequestBody { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            if (request.Content is not null)
            {
                RequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return _response(request);
        }
    }
}
