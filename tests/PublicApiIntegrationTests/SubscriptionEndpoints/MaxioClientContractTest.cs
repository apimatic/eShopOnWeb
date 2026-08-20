using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Subscriptions.Maxio;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class MaxioClientContractTest
{
    [TestMethod]
    public async Task CreateSubscriptionUsesOpenApiRequestShapeAndRemittanceCollection()
    {
        var handler = new InspectingHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://maxio.invalid/")
        };
        var client = new MaxioClient(httpClient);

        var subscription = await client.CreateSubscriptionAsync(
            new MaxioSubscriptionDetails("eshop-pro", 123, "eshop-subscription:user:eshop-pro"),
            CancellationToken.None);

        Assert.AreEqual(456, subscription.Id);
        Assert.AreEqual(HttpMethod.Post, handler.Method);
        Assert.AreEqual("/subscriptions.json", handler.Path);

        using var body = JsonDocument.Parse(handler.Body!);
        var request = body.RootElement.GetProperty("subscription");
        Assert.AreEqual("eshop-pro", request.GetProperty("product_handle").GetString());
        Assert.AreEqual(123, request.GetProperty("customer_id").GetInt32());
        Assert.AreEqual("eshop-subscription:user:eshop-pro", request.GetProperty("reference").GetString());
        Assert.AreEqual("remittance", request.GetProperty("payment_collection_method").GetString());
    }

    private sealed class InspectingHandler : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public string? Path { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            Path = request.RequestUri?.AbsolutePath;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);

            const string responseBody = """
                {
                  "subscription": {
                    "id": 456,
                    "state": "active",
                    "product_price_in_cents": 29900,
                    "current_period_ends_at": "2026-09-21T00:00:00Z",
                    "customer": { "id": 123 },
                    "product": {
                      "id": 789,
                      "name": "Pro Plan",
                      "handle": "eshop-pro",
                      "price_in_cents": 29900,
                      "interval": 1,
                      "interval_unit": "month",
                      "require_credit_card": false
                    }
                  }
                }
                """;

            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
