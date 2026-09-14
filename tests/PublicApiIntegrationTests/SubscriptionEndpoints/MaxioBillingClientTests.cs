using System;
using System.Collections.Generic;
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
public class MaxioBillingClientTests
{
    [TestMethod]
    public async Task UsesBaseUrlVerbatimAndProductFamilyHandleInsteadOfNumericId()
    {
        var handler = new RecordingHandler((_, _) => Json(HttpStatusCode.OK, """
            [
              {
                "product": {
                  "id": 999,
                  "handle": "eshop-pro",
                  "name": "Pro Plan",
                  "description": "Production plan",
                  "price_in_cents": 29900,
                  "interval": 1,
                  "interval_unit": "month",
                  "require_credit_card": false,
                  "archived_at": null,
                  "product_family": { "handle": "family-handle" }
                }
              }
            ]
            """));
        var client = CreateClient(handler);

        var plans = await client.GetPlansAsync(default);

        Assert.AreEqual(1, plans.Count);
        Assert.AreEqual(29900L, plans[0].PriceInCents);
        Assert.AreEqual(
            "https://maxio.test/custom/base/product_families/handle:family-handle/products.json?page=1&per_page=200",
            handler.Requests[0].Uri);
        Assert.AreEqual("Basic", handler.Requests[0].AuthorizationScheme);
        Assert.AreEqual("api-key:X", Encoding.UTF8.GetString(Convert.FromBase64String(handler.Requests[0].AuthorizationParameter!)));
    }

    [TestMethod]
    public async Task CreatesSubscriptionWithStableHandlesAndReference()
    {
        var handler = new RecordingHandler((request, body) => Json(HttpStatusCode.Created, """
            {
              "subscription": {
                "id": 77,
                "reference": "subscription-reference",
                "state": "active",
                "product_price_in_cents": 2900,
                "current_period_ends_at": "2026-09-21T00:00:00Z",
                "next_assessment_at": "2026-09-21T00:00:00Z",
                "customer": { "id": 42, "reference": "customer-reference", "email": "user@example.com" },
                "product": {
                  "handle": "basic-plan",
                  "name": "Basic Plan",
                  "price_in_cents": 2900,
                  "interval": 1,
                  "interval_unit": "month",
                  "product_family": { "handle": "family-handle" }
                }
              }
            }
            """));
        var client = CreateClient(handler);

        var result = await client.CreateSubscriptionAsync(42, "basic-plan", "subscription-reference", default);

        Assert.AreEqual(77L, result.Id);
        using var body = JsonDocument.Parse(handler.Requests[0].Body!);
        var subscription = body.RootElement.GetProperty("subscription");
        Assert.AreEqual("basic-plan", subscription.GetProperty("product_handle").GetString());
        Assert.AreEqual(42L, subscription.GetProperty("customer_id").GetInt64());
        Assert.AreEqual("subscription-reference", subscription.GetProperty("reference").GetString());
        Assert.AreEqual("remittance", subscription.GetProperty("payment_collection_method").GetString());
    }

    private static MaxioBillingClient CreateClient(HttpMessageHandler handler)
    {
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "api-key",
            Subdomain = "ignored",
            ProductFamilyHandle = "family-handle",
            BaseUrl = "https://maxio.test/custom/base"
        });
        return new MaxioBillingClient(new HttpClient(handler), options);
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string body)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string?, HttpResponseMessage> _response;
        public List<RecordedRequest> Requests { get; } = new();

        public RecordingHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> response)
        {
            _response = response;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content == null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(
                request.RequestUri!.ToString(),
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter,
                body));
            return _response(request, body);
        }
    }

    private sealed record RecordedRequest(
        string Uri,
        string? AuthorizationScheme,
        string? AuthorizationParameter,
        string? Body);
}
