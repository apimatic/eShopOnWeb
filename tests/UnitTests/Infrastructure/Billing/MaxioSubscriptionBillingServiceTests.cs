using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioSubscriptionBillingServiceTests
{
    [Fact]
    public async Task ConcurrentSubscribeCreatesOnlyOneRemoteSubscription()
    {
        var handler = new MaxioHandler();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://sandbox.invalid/") };
        var maxioClient = new MaxioClient(httpClient);
        var dbOptions = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new CatalogContext(dbOptions);
        var store = new SubscriptionEnrollmentStore(context);
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "not-a-secret",
            Subdomain = "sandbox",
            ProductFamilyHandle = "family"
        });
        var service = new MaxioSubscriptionBillingService(
            maxioClient,
            store,
            options,
            NullLogger<MaxioSubscriptionBillingService>.Instance);
        var user = new BillingUser("user-1", "shopper@example.com");

        var results = await Task.WhenAll(
            service.SubscribeAsync(user, "pro"),
            service.SubscribeAsync(user, "pro"));

        Assert.Equal(1, handler.SubscriptionCreateCount);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", handler.SubscriptionCreateBody);
        Assert.All(results, result => Assert.Equal(42, result.Id));
        Assert.All(results, result => Assert.Equal("active", result.State));
    }

    private sealed class MaxioHandler : HttpMessageHandler
    {
        private bool _subscriptionCreated;
        public int SubscriptionCreateCount { get; private set; }
        public string SubscriptionCreateBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            if (request.Method == HttpMethod.Get && path.StartsWith("/products/handle/pro.json"))
            {
                return Json(ProductJson);
            }

            if (request.Method == HttpMethod.Get && path.StartsWith("/subscriptions/lookup.json"))
            {
                return _subscriptionCreated
                    ? Json(SubscriptionJson)
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (request.Method == HttpMethod.Get && path.StartsWith("/customers/lookup.json"))
            {
                return Json("""{"customer":{"id":7,"reference":"eshop-user-user-1"}}""");
            }

            if (request.Method == HttpMethod.Post && path == "/subscriptions.json")
            {
                SubscriptionCreateCount++;
                SubscriptionCreateBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                await Task.Delay(25, cancellationToken);
                _subscriptionCreated = true;
                return Json(SubscriptionJson, HttpStatusCode.Created);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string content, HttpStatusCode statusCode = HttpStatusCode.OK) =>
            new(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            };

        private const string ProductJson = """
            {"product":{"id":1,"handle":"pro","name":"Pro","description":"Pro plan","price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false,"archived_at":null,"product_family":{"handle":"family"}}}
            """;

        private const string SubscriptionJson = """
            {"subscription":{"id":42,"reference":"eshop-sub-test","state":"active","product_price_in_cents":29900,"current_period_ends_at":"2026-09-21T00:00:00Z","customer":{"id":7,"reference":"eshop-user-user-1"},"product":{"id":1,"handle":"pro","name":"Pro","price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false,"product_family":{"handle":"family"}}}}
            """;
    }
}
