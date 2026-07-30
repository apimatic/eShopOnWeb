using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionServiceTests
{
    private const string ProductsJson = """
        [{"product":{"id":1,"name":"Pro Plan","handle":"eshop-pro","description":"Everything",
          "price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false,
          "product_family":{"id":9,"handle":"eshop-subscribe"}}},
         {"product":{"id":2,"name":"Basic Plan","handle":"basic-plan",
          "price_in_cents":2900,"interval":1,"interval_unit":"month","require_credit_card":false,
          "product_family":{"id":9,"handle":"eshop-subscribe"}}}]
        """;

    private const string CustomerJson =
        """{"customer":{"id":42,"reference":"user@example.com","email":"user@example.com"}}""";

    private static MaxioSettings Settings() => new()
    {
        ApiKey = "test-key",
        Subdomain = "test",
        ProductFamilyHandle = "eshop-subscribe"
    };

    private static MaxioSubscriptionService BuildService(StubHttpMessageHandler handler, MaxioSettings settings)
    {
        var http = new HttpClient(handler) { BaseAddress = settings.ResolveBaseAddress() };
        var client = new MaxioClient(http);
        return new MaxioSubscriptionService(client, settings, new KeyedAsyncLock(),
            NullLogger<MaxioSubscriptionService>.Instance);
    }

    private static SubscribeRequest Request(string plan = "eshop-pro") =>
        new("user@example.com", "user@example.com", "User", "Example", plan);

    [Fact]
    public async Task GetPlans_MapsFamilyProducts_OrderedByPrice()
    {
        var handler = new StubHttpMessageHandler(_ => (HttpStatusCode.OK, ProductsJson));
        var service = BuildService(handler, Settings());

        var plans = await service.GetPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Equal("basic-plan", plans[0].Handle); // cheapest first
        Assert.Equal("eshop-pro", plans[1].Handle);
        Assert.Equal(29900, plans[1].PriceInCents);
        Assert.Equal(299m, plans[1].Price);
        Assert.False(plans[1].RequiresPaymentMethod);
    }

    [Fact]
    public async Task Subscribe_WhenAlreadyEnrolled_ReturnsExisting_AndDoesNotCreate()
    {
        const string liveSubs = """
            [{"subscription":{"id":999,"state":"active","payment_collection_method":"remittance",
              "currency":"USD","current_period_ends_at":"2026-08-29T00:00:00Z",
              "next_assessment_at":"2026-08-29T00:00:00Z","created_at":"2026-07-29T00:00:00Z",
              "product":{"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,
              "interval":1,"interval_unit":"month"}}}]
            """;

        var handler = new StubHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.Contains("/products.json")) return (HttpStatusCode.OK, ProductsJson);
            if (path.Contains("/lookup.json")) return (HttpStatusCode.OK, CustomerJson);
            if (path.Contains("/subscriptions.json")) return (HttpStatusCode.OK, liveSubs);
            return (HttpStatusCode.InternalServerError, "unexpected call");
        });
        var service = BuildService(handler, Settings());

        var result = await service.SubscribeAsync(Request());

        Assert.True(result.AlreadyExisted);
        Assert.Equal(999, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);
        // Idempotency guarantee: no POST to create a subscription was issued.
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post && r.Path.Contains("subscriptions.json"));
    }

    [Fact]
    public async Task Subscribe_WhenNotEnrolled_CreatesSubscription()
    {
        const string created = """
            {"subscription":{"id":1001,"state":"active","payment_collection_method":"remittance",
              "currency":"USD","next_assessment_at":"2026-08-29T00:00:00Z","created_at":"2026-07-29T00:00:00Z",
              "product":{"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,
              "interval":1,"interval_unit":"month"}}}
            """;

        var handler = new StubHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.Contains("/products.json")) return (HttpStatusCode.OK, ProductsJson);
            if (path.Contains("/lookup.json")) return (HttpStatusCode.OK, CustomerJson);
            if (req.Method == HttpMethod.Get && path.Contains("/subscriptions.json")) return (HttpStatusCode.OK, "[]");
            if (req.Method == HttpMethod.Post && path.Contains("subscriptions.json")) return (HttpStatusCode.Created, created);
            return (HttpStatusCode.InternalServerError, "unexpected call");
        });
        var service = BuildService(handler, Settings());

        var result = await service.SubscribeAsync(Request());

        Assert.False(result.AlreadyExisted);
        Assert.Equal(1001, result.Subscription.Id);
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Post && r.Path.Contains("subscriptions.json"));
    }

    [Fact]
    public async Task Subscribe_UnknownPlan_ThrowsNotFound()
    {
        var handler = new StubHttpMessageHandler(_ => (HttpStatusCode.OK, ProductsJson));
        var service = BuildService(handler, Settings());

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => service.SubscribeAsync(Request("does-not-exist")));

        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task GetSubscriptions_WhenNoCustomer_ReturnsEmpty()
    {
        var handler = new StubHttpMessageHandler(req =>
            req.RequestUri!.AbsolutePath.Contains("/lookup.json")
                ? (HttpStatusCode.NotFound, "")
                : (HttpStatusCode.InternalServerError, "unexpected call"));
        var service = BuildService(handler, Settings());

        var subs = await service.GetSubscriptionsAsync("user@example.com");

        Assert.Empty(subs);
    }
}
