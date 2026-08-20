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
public class MaxioBillingClientTests
{
    [TestMethod]
    public async Task ListProductsUsesFamilyHandleAndBasicAuthentication()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK,
            "[{\"product\":{\"id\":7,\"name\":\"Basic\",\"handle\":\"basic\",\"price_in_cents\":2900,\"interval\":1,\"interval_unit\":\"month\",\"require_credit_card\":false,\"product_family\":{\"handle\":\"family\"}}}]"));
        var client = CreateClient(handler);

        var products = await client.ListProductsAsync(CancellationToken.None);

        Assert.AreEqual(1, products.Count);
        Assert.AreEqual("basic", products[0].Handle);
        Assert.AreEqual("https://billing.test/root/product_families/handle:family/products.json?per_page=200",
            handler.RequestUri!.ToString());
        Assert.AreEqual("Basic", handler.AuthorizationScheme);
        Assert.AreEqual("test-key:X", Encoding.UTF8.GetString(
            Convert.FromBase64String(handler.AuthorizationParameter!)));
    }

    [TestMethod]
    public async Task FindCustomerReturnsNullOnNotFound()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.NotFound, "{}"));
        var client = CreateClient(handler);

        var customer = await client.FindCustomerAsync("eshop:user/1", CancellationToken.None);

        Assert.IsNull(customer);
        StringAssert.Contains(handler.RequestUri!.Query, "reference=eshop%3Auser%2F1");
    }

    [TestMethod]
    public async Task CreateSubscriptionSendsDocumentedHandleReferenceAndUniquenessFields()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.Created,
            "{\"subscription\":{\"id\":42,\"state\":\"active\",\"product_price_in_cents\":29900,\"next_assessment_at\":\"2026-09-21T00:00:00Z\",\"product\":{\"name\":\"Pro\",\"handle\":\"eshop-pro\",\"interval\":1,\"interval_unit\":\"month\",\"product_family\":{\"handle\":\"family\"}}}}"));
        var client = CreateClient(handler);

        var subscription = await client.CreateSubscriptionAsync("customer-ref", "eshop-pro",
            "subscription-ref", "unique-token", CancellationToken.None);

        Assert.AreEqual(42, subscription.Id);
        StringAssert.Contains(handler.Body!, "\"product_handle\":\"eshop-pro\"");
        StringAssert.Contains(handler.Body!, "\"customer_reference\":\"customer-ref\"");
        StringAssert.Contains(handler.Body!, "\"reference\":\"subscription-ref\"");
        StringAssert.Contains(handler.Body!, "\"payment_collection_method\":\"remittance\"");
        StringAssert.Contains(handler.Body!, "\"uniqueness_token\":\"unique-token\"");
    }

    private static MaxioBillingClient CreateClient(HttpMessageHandler handler)
    {
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "unused",
            ProductFamilyHandle = "family",
            BaseUrl = "https://billing.test/root"
        });
        return new MaxioBillingClient(new HttpClient(handler), options);
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string content) => new(statusCode)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _response;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> response)
        {
            _response = response;
        }

        public Uri? RequestUri { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return _response(request);
        }
    }
}
