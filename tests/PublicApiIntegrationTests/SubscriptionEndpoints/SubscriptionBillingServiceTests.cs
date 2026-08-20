using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionBillingServiceTests
{
    [TestMethod]
    public async Task SubscribeIsIdempotentAndPersistsCorrelation()
    {
        var handler = new MaxioFixtureHandler();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var client = new MaxioClient(httpClient);
        var dbOptions = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new CatalogContext(dbOptions);
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "not-a-secret",
            Subdomain = "example",
            ProductFamilyHandle = "family-under-test"
        });
        var service = new SubscriptionBillingService(context, client, options);
        var user = new SubscriptionUser("user-123", "shopper@example.test", "shopper@example.test");

        var first = await service.SubscribeAsync(user, "eshop-pro");
        var second = await service.SubscribeAsync(user, "eshop-pro");

        Assert.IsFalse(first.AlreadyExisted);
        Assert.IsTrue(second.AlreadyExisted);
        Assert.AreEqual(1, handler.CreateCustomerCalls);
        Assert.AreEqual(1, handler.CreateSubscriptionCalls);
        Assert.AreEqual(29900, second.Subscription.PriceInCents);
        Assert.AreEqual("active", second.Subscription.State);
        Assert.IsNotNull(second.Subscription.NextBillingAt);

        var record = await context.SubscriptionRecords.SingleAsync();
        Assert.AreEqual("user-123", record.UserId);
        Assert.AreEqual("eshop-pro", record.ProductHandle);
        Assert.AreEqual(7001, record.MaxioCustomerId);
        Assert.AreEqual(8001, record.MaxioSubscriptionId);
    }

    [TestMethod]
    public async Task ListPlansReturnsOnlyActiveConfiguredFamilyProducts()
    {
        var handler = new MaxioFixtureHandler();
        var client = new MaxioClient(new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") });
        var dbOptions = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new CatalogContext(dbOptions);
        var service = new SubscriptionBillingService(context, client, Options.Create(new MaxioOptions
        {
            ApiKey = "not-a-secret",
            Subdomain = "example",
            ProductFamilyHandle = "family-under-test"
        }));

        var plans = await service.ListPlansAsync();

        Assert.AreEqual(1, plans.Count);
        Assert.AreEqual("eshop-pro", plans[0].Handle);
        Assert.AreEqual(29900, plans[0].PriceInCents);
        Assert.IsFalse(plans[0].RequiresPaymentMethod);
    }

    private sealed class MaxioFixtureHandler : HttpMessageHandler
    {
        private bool _customerCreated;
        private bool _subscriptionCreated;

        public int CreateCustomerCalls { get; private set; }
        public int CreateSubscriptionCalls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.EndsWith("/products.json", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, ProductsJson);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/customers/lookup.json", StringComparison.Ordinal))
            {
                return _customerCreated
                    ? Json(HttpStatusCode.OK, CustomerJson)
                    : Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/customers.json", StringComparison.Ordinal))
            {
                _customerCreated = true;
                CreateCustomerCalls++;
                return Json(HttpStatusCode.OK, CustomerJson);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/customers/7001/subscriptions.json", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, _subscriptionCreated ? $"[{SubscriptionJson}]" : "[]");
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json", StringComparison.Ordinal))
            {
                _subscriptionCreated = true;
                CreateSubscriptionCalls++;
                return Json(HttpStatusCode.Created, SubscriptionJson);
            }

            throw new AssertFailedException($"Unexpected request: {request.Method} {request.RequestUri}");
        }

        private static Task<HttpResponseMessage> Json(HttpStatusCode status, string json) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });

        private const string CustomerJson = """
            {"customer":{"id":7001,"reference":"user-123"}}
            """;

        private const string ProductJson = """
            {"id":7101,"name":"Pro Plan","handle":"eshop-pro","description":"Pro", "price_in_cents":29900,"interval":1,"interval_unit":"month","archived_at":null,"require_credit_card":false,"product_family":{"id":3001,"handle":"family-under-test"}}
            """;

        private const string ProductsJson = """
            [
              {"product":{"id":7101,"name":"Pro Plan","handle":"eshop-pro","description":"Pro","price_in_cents":29900,"interval":1,"interval_unit":"month","archived_at":null,"require_credit_card":false,"product_family":{"id":3001,"handle":"family-under-test"}}},
              {"product":{"id":7102,"name":"Other","handle":"other","description":null,"price_in_cents":100,"interval":1,"interval_unit":"month","archived_at":null,"require_credit_card":false,"product_family":{"id":3002,"handle":"other-family"}}},
              {"product":{"id":7103,"name":"Archived","handle":"archived","description":null,"price_in_cents":100,"interval":1,"interval_unit":"month","archived_at":"2026-01-01T00:00:00Z","require_credit_card":false,"product_family":{"id":3001,"handle":"family-under-test"}}}
            ]
            """;

        private const string SubscriptionJson = """
            {"subscription":{"id":8001,"state":"active","product_price_in_cents":29900,"current_period_ends_at":"2026-09-21T00:00:00Z","next_assessment_at":"2026-09-21T00:00:00Z","reference":"eshop:user-123:eshop-pro","currency":"USD","customer":{"id":7001,"reference":"user-123"},"product":
            """ + ProductJson + "}}";
    }
}
