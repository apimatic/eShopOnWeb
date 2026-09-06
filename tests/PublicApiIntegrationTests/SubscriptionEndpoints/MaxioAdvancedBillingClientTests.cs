using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class MaxioAdvancedBillingClientTests
{
    [TestMethod]
    public async Task ListsNonArchivedPlansFromConfiguredProductFamilyHandle()
    {
        var handler = new StubHttpMessageHandler(new Queue<HttpResponseMessage>(new[]
        {
            Json(HttpStatusCode.OK, "{\"product_family\":{\"id\":42,\"handle\":\"plans\"}}"),
            Json(HttpStatusCode.OK, "[{\"product\":{\"handle\":\"pro\",\"name\":\"Pro\",\"description\":\"Plan\",\"price_in_cents\":29900,\"interval\":1,\"interval_unit\":\"month\",\"archived_at\":null}}]")
        }));
        var client = CreateClient(handler);

        var plans = await client.GetPlansAsync(CancellationToken.None);

        Assert.AreEqual(1, plans.Count);
        Assert.AreEqual("pro", plans[0].Handle);
        Assert.AreEqual(29900L, plans[0].PriceInCents);
        CollectionAssert.AreEqual(new[]
        {
            "/product_families/handle%3Aplans.json",
            "/product_families/42/products.json"
        }, handler.Paths);
    }

    [TestMethod]
    public async Task CreatesRemittanceSubscriptionUsingMaxioContractFields()
    {
        var handler = new StubHttpMessageHandler(new Queue<HttpResponseMessage>(new[]
        {
            Json(HttpStatusCode.Created, "{\"subscription\":{\"id\":99,\"state\":\"active\",\"product_price_in_cents\":29900,\"next_assessment_at\":\"2026-10-01T00:00:00Z\",\"current_period_ends_at\":\"2026-10-01T00:00:00Z\",\"product\":{\"handle\":\"pro\",\"name\":\"Pro\",\"description\":null,\"price_in_cents\":29900,\"interval\":1,\"interval_unit\":\"month\",\"archived_at\":null}}}")
        }));
        var client = CreateClient(handler);

        var subscription = await client.CreateSubscriptionAsync("pro", 77, "eshopweb-subscription-1", CancellationToken.None);

        Assert.AreEqual(99L, subscription.Id);
        StringAssert.Contains(handler.RequestBodies[0], "\"product_handle\":\"pro\"");
        StringAssert.Contains(handler.RequestBodies[0], "\"customer_id\":77");
        StringAssert.Contains(handler.RequestBodies[0], "\"payment_collection_method\":\"remittance\"");
    }

    private static MaxioAdvancedBillingClient CreateClient(StubHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://sandbox.example/") };
        var options = Options.Create(new MaxioOptions { ApiKey = "test-key", Subdomain = "sandbox", ProductFamilyHandle = "plans" });
        return new MaxioAdvancedBillingClient(httpClient, options);
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string content) => new(statusCode)
    {
        Content = new StringContent(content)
    };

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;
        public List<string> Paths { get; } = new();
        public List<string> RequestBodies { get; } = new();

        public StubHttpMessageHandler(Queue<HttpResponseMessage> responses) => _responses = responses;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            RequestBodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return _responses.Dequeue();
        }
    }
}
