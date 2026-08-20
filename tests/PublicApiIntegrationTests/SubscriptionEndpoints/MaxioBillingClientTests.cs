using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class MaxioBillingClientTests
{
    [TestMethod]
    public async Task MapsPlansAndUsesFamilyHandleAndBasicAuthentication()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, """
            [{"product":{"id":42,"name":"Pro","handle":"pro","description":"Plan","price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false,"archived_at":null}}]
            """));
        var client = CreateClient(handler);

        var plans = await client.GetProductsAsync("family", CancellationToken.None);

        Assert.AreEqual(1, plans.Count);
        Assert.AreEqual(29900, plans[0].PriceInCents);
        Assert.AreEqual("product_families/handle:family/products.json?per_page=200", handler.Requests.Single().PathAndQuery.TrimStart('/'));
        Assert.AreEqual("Basic", handler.Requests.Single().AuthorizationScheme);
    }

    [TestMethod]
    public async Task SerializesCreateSubscriptionUsingDocumentedMaxioFields()
    {
        var handler = new RecordingHandler(request => Json(HttpStatusCode.Created, """
            {"subscription":{"id":99,"reference":"sub-ref","state":"active","product_price_in_cents":29900,"current_period_ends_at":"2026-09-21T00:00:00Z","customer":{"id":7,"reference":"customer-ref","email":"buyer@example.com"},"product":{"id":42,"name":"Pro","handle":"pro","price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false}}}
            """));
        var client = CreateClient(handler);

        var result = await client.CreateSubscriptionAsync(
            new CreateMaxioSubscription("pro", "customer-ref", "sub-ref", "remittance", Guid.Parse("11111111-1111-1111-1111-111111111111")),
            CancellationToken.None);

        Assert.AreEqual("active", result.State);
        using var body = JsonDocument.Parse(handler.Requests.Single().Body!);
        Assert.AreEqual("pro", body.RootElement.GetProperty("subscription").GetProperty("product_handle").GetString());
        Assert.AreEqual("customer-ref", body.RootElement.GetProperty("subscription").GetProperty("customer_reference").GetString());
        Assert.AreEqual("sub-ref", body.RootElement.GetProperty("subscription").GetProperty("reference").GetString());
        Assert.AreEqual("remittance", body.RootElement.GetProperty("subscription").GetProperty("payment_collection_method").GetString());
        Assert.IsTrue(body.RootElement.TryGetProperty("uniqueness_token", out _));
    }

    private static MaxioBillingClient CreateClient(HttpMessageHandler handler)
    {
        return new MaxioBillingClient(new HttpClient(handler), new MaxioOptions
        {
            ApiKey = "test-key",
            BaseUrl = "https://billing.example.test/",
            ProductFamilyHandle = "family"
        });
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

        public List<RecordedRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(
                request.RequestUri!.PathAndQuery,
                request.Headers.Authorization?.Scheme,
                body));
            return _response(request);
        }
    }

    private sealed record RecordedRequest(string PathAndQuery, string? AuthorizationScheme, string? Body);
}
