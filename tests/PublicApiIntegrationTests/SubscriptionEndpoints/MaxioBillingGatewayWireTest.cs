using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class MaxioBillingGatewayWireTest
{
    [TestMethod]
    public async Task ListsConfiguredFamilyPlansUsingStableHandleAndPagedRoute()
    {
        var responses = new Queue<string>(new[]
        {
            """[{"product_family":{"id":123,"name":"eShop Subscribe","handle":"eshop-subscribe","archived_at":null}}]""",
            """[{"product":{"id":456,"name":"Pro Plan","handle":"eshop-pro","description":"Pro subscription","price_in_cents":29900,"interval":1,"interval_unit":"month","archived_at":null,"require_credit_card":false,"product_family":{"id":123,"name":"eShop Subscribe","handle":"eshop-subscribe","archived_at":null}}}]"""
        });
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, responses.Dequeue()));
        var gateway = CreateGateway(handler);

        var plans = await gateway.ListPlansAsync(default);

        Assert.AreEqual(1, plans.Count);
        Assert.AreEqual("eshop-pro", plans[0].Handle);
        Assert.AreEqual(29900L, plans[0].PriceInCents);
        Assert.AreEqual(2, handler.Requests.Count);
        Assert.AreEqual("/product_families.json", handler.Requests[0].Path);
        Assert.AreEqual(string.Empty, handler.Requests[0].Query.TrimStart('?'));
        Assert.AreEqual("/product_families/123/products.json", handler.Requests[1].Path);
        CollectionAssert.AreEquivalent(
            new[] { "page=1", "per_page=100", "include_archived=false" },
            handler.Requests[1].Query.TrimStart('?').Split('&'));
    }

    [TestMethod]
    public async Task CreatesSubscriptionWithOnlyStableHandlesAndDeterministicReference()
    {
        const string responseJson = """{"subscription":{"id":789,"state":"active","product_price_in_cents":29900,"current_period_ends_at":"2026-09-27T00:00:00Z","next_assessment_at":"2026-09-27T00:00:00Z","current_billing_amount_in_cents":29900,"reference":"subscription-user-123-eshop-pro","currency":"USD","customer":{"id":321,"reference":"user-123","first_name":"Ada","last_name":"Lovelace","email":"ada@example.test"},"product":{"id":456,"name":"Pro Plan","handle":"eshop-pro","description":"Pro subscription","price_in_cents":29900,"interval":1,"interval_unit":"month","archived_at":null,"require_credit_card":false,"product_family":{"id":123,"name":"eShop Subscribe","handle":"eshop-subscribe","archived_at":null}}}}""";
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.Created, responseJson));
        var gateway = CreateGateway(handler);

        var subscription = await gateway.CreateSubscriptionAsync(
            "eshop-pro",
            "user-123",
            "subscription-user-123-eshop-pro",
            default);

        Assert.AreEqual(789, subscription.Id);
        Assert.AreEqual(1, handler.Requests.Count);
        Assert.AreEqual(HttpMethod.Post, handler.Requests[0].Method);
        Assert.AreEqual("/subscriptions.json", handler.Requests[0].Path);
        StringAssert.Contains(handler.Requests[0].Body, "\"product_handle\":\"eshop-pro\"");
        StringAssert.Contains(handler.Requests[0].Body, "\"customer_reference\":\"user-123\"");
        StringAssert.Contains(handler.Requests[0].Body, "\"reference\":\"subscription-user-123-eshop-pro\"");
        StringAssert.Contains(handler.Requests[0].Body, "\"payment_collection_method\":\"remittance\"");
        Assert.IsFalse(handler.Requests[0].Body.Contains("product_id", StringComparison.Ordinal));
        Assert.IsFalse(handler.Requests[0].Body.Contains("customer_id", StringComparison.Ordinal));
    }

    private static MaxioBillingGateway CreateGateway(HttpMessageHandler handler)
    {
        var clientOptions = new MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials { Username = "test-api-key", Password = "x" }
        };
        clientOptions.Server.Production.Us.BaseUrl = "https://maxio.test";
        var client = new MaxioAdvancedBilling.MaxioAdvancedBillingClient(new HttpClient(handler), clientOptions);
        return new MaxioBillingGateway(
            client,
            NullLogger<MaxioBillingGateway>.Instance,
            Options.Create(new MaxioOptions
            {
                ApiKey = "test-api-key",
                Subdomain = "test-site",
                ProductFamilyHandle = "eshop-subscribe"
            }));
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string json) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        internal RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        internal List<RecordedRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!.AbsolutePath,
                request.RequestUri.Query,
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));
            return _responder(request);
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, string Path, string Query, string Body);
}
