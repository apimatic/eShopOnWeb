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
public class MaxioClientContractTests
{
    [TestMethod]
    public async Task ListsFamilyProductsUsingSpecPathPaginationAndBasicAuth()
    {
        var handler = new RecordingHandler(_ => JsonResponse("""
            [{"product":{"id":7,"name":"Pro","handle":"eshop-pro","description":"Plan","price_in_cents":29900,"interval":1,"interval_unit":"month","archived_at":null,"require_credit_card":false,"product_family":{"id":3,"name":"Plans","handle":"family"}}}]
            """));
        var client = CreateClient(handler);

        var products = await client.ListProductsForFamilyAsync("family", CancellationToken.None);

        Assert.AreEqual(1, products.Count);
        Assert.AreEqual("eshop-pro", products[0].Handle);
        Assert.AreEqual(HttpMethod.Get, handler.Requests[0].Method);
        StringAssert.Contains(handler.Requests[0].Uri, "/product_families/handle%3Afamily/products.json");
        StringAssert.Contains(handler.Requests[0].Uri, "page=1&per_page=200&include_archived=false");
        Assert.AreEqual(
            Convert.ToBase64String(Encoding.UTF8.GetBytes("test-api-key:x")),
            handler.Requests[0].AuthorizationParameter);
    }

    [TestMethod]
    public async Task CreatesSubscriptionUsingSpecSnakeCaseEnvelope()
    {
        var handler = new RecordingHandler(_ => JsonResponse("""
            {"subscription":{"id":11,"state":"active","product_price_in_cents":29900,"current_period_ends_at":"2026-09-21T00:00:00Z","next_assessment_at":"2026-09-21T00:00:00Z","created_at":"2026-08-21T00:00:00Z","reference":"ref","currency":"USD","customer":{"id":5,"first_name":"demo","last_name":"eShopOnWeb","email":"demo@example.com","reference":"customer-ref"},"product":{"id":7,"name":"Pro","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false,"product_family":{"id":3,"name":"Plans","handle":"family"}}}}
            """));
        var client = CreateClient(handler);

        var subscription = await client.CreateSubscriptionAsync(new MaxioCreateSubscription
        {
            ProductHandle = "eshop-pro",
            CustomerId = 5,
            Reference = "ref",
            PaymentCollectionMethod = "remittance"
        }, CancellationToken.None);

        Assert.AreEqual(11, subscription.Id);
        Assert.AreEqual(HttpMethod.Post, handler.Requests[0].Method);
        StringAssert.EndsWith(handler.Requests[0].Uri, "/subscriptions.json");
        using var body = JsonDocument.Parse(handler.Requests[0].Body!);
        var request = body.RootElement.GetProperty("subscription");
        Assert.AreEqual("eshop-pro", request.GetProperty("product_handle").GetString());
        Assert.AreEqual(5, request.GetProperty("customer_id").GetInt32());
        Assert.AreEqual("ref", request.GetProperty("reference").GetString());
        Assert.AreEqual("remittance", request.GetProperty("payment_collection_method").GetString());
        Assert.IsFalse(request.TryGetProperty("productHandle", out _));
    }

    private static MaxioClient CreateClient(HttpMessageHandler handler)
    {
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-api-key",
            Subdomain = "unused",
            ProductFamilyHandle = "family",
            BaseUrl = "https://maxio.test/base"
        });
        return new MaxioClient(new HttpClient(handler), options);
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _response;

        internal RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> response)
        {
            _response = response;
        }

        internal List<RecordedRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!.AbsoluteUri,
                request.Headers.Authorization?.Parameter,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            return _response(request);
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        string Uri,
        string? AuthorizationParameter,
        string? Body);
}
