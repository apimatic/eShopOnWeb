using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Maxio;

[TestClass]
public class MaxioBillingServiceTests
{
    [TestMethod]
    public async Task RepeatedSubscribeCreatesCustomerAndSubscriptionOnlyOnce()
    {
        var customerPostCount = 0;
        var subscriptionPostCount = 0;

        var handler = new StubHttpMessageHandler(async request =>
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path.StartsWith("/product_families/", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, ProductsJson);
            if (path.StartsWith("/subscriptions/lookup.json", StringComparison.Ordinal))
                return subscriptionPostCount == 0 ? Json(HttpStatusCode.NotFound, "{}") : Json(HttpStatusCode.OK, SubscriptionJson);
            if (path.StartsWith("/customers/lookup.json", StringComparison.Ordinal))
                return Json(HttpStatusCode.NotFound, "{}");
            if (request.Method == HttpMethod.Post && path == "/customers.json")
            {
                customerPostCount++;
                return Json(HttpStatusCode.OK, "{\"customer\":{\"id\":42}}");
            }
            if (request.Method == HttpMethod.Post && path == "/subscriptions.json")
            {
                subscriptionPostCount++;
                var body = await request.Content!.ReadAsStringAsync();
                StringAssert.Contains(body, "\"payment_collection_method\":\"remittance\"");
                return Json(HttpStatusCode.Created, SubscriptionJson);
            }

            Assert.Fail($"Unexpected Maxio request: {request.Method} {path}");
            return Json(HttpStatusCode.InternalServerError, "{}");
        });

        var service = CreateService(handler);
        var user = new MaxioUser("user-123", "shopper@example.test", "Shopper", "Example");

        var attempts = await Task.WhenAll(
            service.SubscribeAsync(user, "eshop-pro", CancellationToken.None),
            service.SubscribeAsync(user, "eshop-pro", CancellationToken.None));
        var first = attempts[0];
        var second = attempts[1];

        Assert.IsNotNull(first);
        Assert.IsNotNull(second);
        Assert.AreEqual(123, first.Id);
        Assert.AreEqual(first.Id, second.Id);
        Assert.AreEqual(1, customerPostCount);
        Assert.AreEqual(1, subscriptionPostCount);
    }

    [TestMethod]
    public async Task GetPlansExcludesArchivedProductsAndOrdersByPrice()
    {
        var handler = new StubHttpMessageHandler(_ => Task.FromResult(Json(HttpStatusCode.OK, ProductsJson)));
        var plans = await CreateService(handler).GetPlansAsync(CancellationToken.None);

        Assert.AreEqual(2, plans.Count);
        Assert.AreEqual("basic-plan", plans[0].Handle);
        Assert.AreEqual(2900, plans[0].PriceInCents);
        Assert.AreEqual("eshop-pro", plans[1].Handle);
        Assert.IsFalse(plans[1].RequiresPaymentMethod);
    }

    private static MaxioBillingService CreateService(HttpMessageHandler handler)
    {
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "unit-test-key",
            BaseUrl = "https://maxio.example.test",
            ProductFamilyHandle = "test-family"
        });
        return new MaxioBillingService(new HttpClient(handler), options);
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string json)
        => new(statusCode) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private const string ProductsJson = """
        [
          {"product":{"id":2,"name":"Pro","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month","archived_at":null,"require_credit_card":false}},
          {"product":{"id":1,"name":"Basic","handle":"basic-plan","price_in_cents":2900,"interval":1,"interval_unit":"month","archived_at":null,"require_credit_card":false}},
          {"product":{"id":3,"name":"Old","handle":"old-plan","price_in_cents":100,"interval":1,"interval_unit":"month","archived_at":"2025-01-01T00:00:00Z","require_credit_card":false}}
        ]
        """;

    private const string SubscriptionJson = """
        {"subscription":{"id":123,"state":"active","reference":"eshop:user-123:eshop-pro","product_price_in_cents":29900,"current_period_ends_at":"2026-09-21T00:00:00Z","product":{"name":"Pro","handle":"eshop-pro","interval":1,"interval_unit":"month"}}}
        """;

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;
        public StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) => _handler = handler;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _handler(request);
    }
}
