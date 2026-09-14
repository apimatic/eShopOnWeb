using System;
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
public sealed class MaxioClientContractTest
{
    [TestMethod]
    public async Task CreateSubscriptionUsesOverrideBasicAuthHandlesAndUniquenessToken()
    {
        var handler = new CaptureHandler();
        using var httpClient = new HttpClient(handler);
        var client = new MaxioClient(httpClient, Options.Create(new MaxioOptions
        {
            ApiKey = "test-api-key",
            Subdomain = "ignored-subdomain",
            ProductFamilyHandle = "family",
            BaseUrl = "https://override.example.test/custom/base"
        }));

        var result = await client.CreateSubscriptionAsync(new CreateMaxioSubscription(
            "basic-plan", "customer-ref", "subscription-ref", "unique-token", "remittance"), CancellationToken.None);

        Assert.AreEqual(42L, result.Id);
        Assert.AreEqual("https://override.example.test/custom/base/subscriptions.json", handler.Uri!.ToString());
        Assert.AreEqual("Basic", handler.AuthorizationScheme);
        Assert.AreEqual("test-api-key:X", Encoding.UTF8.GetString(Convert.FromBase64String(handler.AuthorizationParameter!)));

        using var document = JsonDocument.Parse(handler.Body!);
        Assert.AreEqual("unique-token", document.RootElement.GetProperty("uniqueness_token").GetString());
        var subscription = document.RootElement.GetProperty("subscription");
        Assert.AreEqual("basic-plan", subscription.GetProperty("product_handle").GetString());
        Assert.AreEqual("customer-ref", subscription.GetProperty("customer_reference").GetString());
        Assert.AreEqual("subscription-ref", subscription.GetProperty("reference").GetString());
        Assert.AreEqual("remittance", subscription.GetProperty("payment_collection_method").GetString());
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }
        public string? Body { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Uri = request.RequestUri;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;

            const string json = """
                {
                  "subscription": {
                    "id": 42,
                    "reference": "subscription-ref",
                    "state": "active",
                    "product_price_in_cents": 2900,
                    "next_assessment_at": "2026-09-20T12:00:00Z",
                    "customer": { "id": 7, "reference": "customer-ref" },
                    "product": {
                      "id": 1,
                      "name": "Basic Plan",
                      "handle": "basic-plan",
                      "price_in_cents": 2900,
                      "interval": 1,
                      "interval_unit": "month",
                      "archived_at": null,
                      "require_credit_card": false,
                      "product_family": { "handle": "family" }
                    }
                  }
                }
                """;
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }
}
