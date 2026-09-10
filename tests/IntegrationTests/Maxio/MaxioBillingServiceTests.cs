using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Maxio;

public class MaxioBillingServiceTests
{
    private static readonly MaxioSettings Settings = new()
    {
        ApiKey = "test-key",
        Subdomain = "test-site",
        ProductFamilyHandle = "eshop-subscribe"
    };

    private static MaxioBillingService CreateService(StubHttpMessageHandler handler)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://test-site.chargify.com/") };
        return new MaxioBillingService(client, Options.Create(Settings), NullLogger<MaxioBillingService>.Instance);
    }

    private static string SubscriptionJson(long id, string state, string handle, int priceCents = 29900) =>
        """
        {"subscription":{"id":__ID__,"state":"__STATE__","product_price_in_cents":__PRICE__,
        "current_period_ends_at":"2026-10-09T04:34:07+05:00","created_at":"2026-09-09T04:34:07+05:00",
        "product":{"id":1,"name":"Pro Plan","handle":"__HANDLE__","price_in_cents":__PRICE__,"interval":1,"interval_unit":"month"},
        "customer":{"id":123,"reference":"user@example.com"}}}
        """
        .Replace("__ID__", id.ToString())
        .Replace("__STATE__", state)
        .Replace("__PRICE__", priceCents.ToString())
        .Replace("__HANDLE__", handle);

    [Fact]
    public async Task GetPlansAsync_maps_and_orders_products_by_price()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(HttpStatusCode.OK, """
            [
              {"product":{"id":2,"name":"Pro Plan","handle":"eshop-pro","description":"Pro","price_in_cents":29900,"interval":1,"interval_unit":"month"}},
              {"product":{"id":1,"name":"Basic Plan","handle":"basic-plan","price_in_cents":2900,"interval":1,"interval_unit":"month"}},
              {"product":{"id":3,"name":"Archived","handle":"old","price_in_cents":100,"interval":1,"interval_unit":"month","archived_at":"2020-01-01T00:00:00Z"}}
            ]
            """));
        var service = CreateService(handler);

        var plans = await service.GetPlansAsync();

        Assert.Equal(2, plans.Count); // archived filtered out
        Assert.Equal("basic-plan", plans[0].Handle); // ordered by price ascending
        Assert.Equal(29.00m, plans[0].Price);
        Assert.Equal("eshop-pro", plans[1].Handle);
        Assert.Equal(299.00m, plans[1].Price);
    }

    [Fact]
    public async Task SubscribeAsync_creates_customer_and_subscription_when_none_exist()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.PathAndQuery;
            if (request.Method == HttpMethod.Get && path.Contains("customers/lookup"))
                return StubHttpMessageHandler.Json(HttpStatusCode.NotFound, "{}");
            if (request.Method == HttpMethod.Post && path.EndsWith("customers.json"))
                return StubHttpMessageHandler.Json(HttpStatusCode.Created, """{"customer":{"id":123,"reference":"new@example.com","email":"new@example.com"}}""");
            if (request.Method == HttpMethod.Get && path.Contains("/subscriptions.json"))
                return StubHttpMessageHandler.Json(HttpStatusCode.OK, "[]");
            if (request.Method == HttpMethod.Post && path.EndsWith("subscriptions.json"))
                return StubHttpMessageHandler.Json(HttpStatusCode.Created, SubscriptionJson(999, "active", "eshop-pro"));
            return StubHttpMessageHandler.Json(HttpStatusCode.InternalServerError, "{}");
        });
        var service = CreateService(handler);

        var result = await service.SubscribeAsync(new SubscribeCommand("new@example.com", "new@example.com", "New", "User", "eshop-pro"));

        Assert.False(result.AlreadyExisted);
        Assert.True(result.CustomerCreated);
        Assert.Equal(999, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal(1, handler.CountOf("POST", "customers.json"));
        Assert.Equal(1, handler.CountOf("POST", "subscriptions.json"));
    }

    [Fact]
    public async Task SubscribeAsync_is_idempotent_when_live_subscription_exists()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.PathAndQuery;
            if (request.Method == HttpMethod.Get && path.Contains("customers/lookup"))
                return StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"customer":{"id":123,"reference":"user@example.com"}}""");
            if (request.Method == HttpMethod.Get && path.Contains("/subscriptions.json"))
                return StubHttpMessageHandler.Json(HttpStatusCode.OK, $"[{SubscriptionJson(555, "active", "eshop-pro")}]");
            return StubHttpMessageHandler.Json(HttpStatusCode.InternalServerError, "{}");
        });
        var service = CreateService(handler);

        var result = await service.SubscribeAsync(new SubscribeCommand("user@example.com", "user@example.com", "User", "Test", "eshop-pro"));

        Assert.True(result.AlreadyExisted);
        Assert.Equal(555, result.Subscription.Id);
        Assert.Equal(0, handler.CountOf("POST", "customers.json")); // existing customer reused
        Assert.Equal(0, handler.CountOf("POST", "subscriptions.json")); // no duplicate created
    }

    [Fact]
    public async Task SubscribeAsync_allows_resubscribe_when_previous_was_canceled()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.PathAndQuery;
            if (request.Method == HttpMethod.Get && path.Contains("customers/lookup"))
                return StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"customer":{"id":123,"reference":"user@example.com"}}""");
            if (request.Method == HttpMethod.Get && path.Contains("/subscriptions.json"))
                return StubHttpMessageHandler.Json(HttpStatusCode.OK, $"[{SubscriptionJson(555, "canceled", "eshop-pro")}]");
            if (request.Method == HttpMethod.Post && path.EndsWith("subscriptions.json"))
                return StubHttpMessageHandler.Json(HttpStatusCode.Created, SubscriptionJson(777, "active", "eshop-pro"));
            return StubHttpMessageHandler.Json(HttpStatusCode.InternalServerError, "{}");
        });
        var service = CreateService(handler);

        var result = await service.SubscribeAsync(new SubscribeCommand("user@example.com", "user@example.com", "User", "Test", "eshop-pro"));

        Assert.False(result.AlreadyExisted);
        Assert.Equal(777, result.Subscription.Id);
        Assert.Equal(1, handler.CountOf("POST", "subscriptions.json"));
    }

    [Fact]
    public async Task SubscribeAsync_concurrent_double_click_creates_only_one_subscription()
    {
        var created = 0;
        var gate = new object();
        var handler = new StubHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.PathAndQuery;
            if (request.Method == HttpMethod.Get && path.Contains("customers/lookup"))
                return StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"customer":{"id":123,"reference":"race@example.com"}}""");
            if (request.Method == HttpMethod.Get && path.Contains("/subscriptions.json"))
            {
                lock (gate)
                {
                    var body = created > 0 ? $"[{SubscriptionJson(1000, "active", "eshop-pro")}]" : "[]";
                    return StubHttpMessageHandler.Json(HttpStatusCode.OK, body);
                }
            }
            if (request.Method == HttpMethod.Post && path.EndsWith("subscriptions.json"))
            {
                lock (gate) { created++; }
                return StubHttpMessageHandler.Json(HttpStatusCode.Created, SubscriptionJson(1000, "active", "eshop-pro"));
            }
            return StubHttpMessageHandler.Json(HttpStatusCode.InternalServerError, "{}");
        });
        var service = CreateService(handler);
        var command = new SubscribeCommand("race@example.com", "race@example.com", "Race", "User", "eshop-pro");

        var t1 = Task.Run(() => service.SubscribeAsync(command));
        var t2 = Task.Run(() => service.SubscribeAsync(command));
        var results = await Task.WhenAll(t1, t2);

        Assert.Equal(1, handler.CountOf("POST", "subscriptions.json")); // the in-process lock + pre-check prevent a duplicate
        Assert.All(results, r => Assert.Equal(1000, r.Subscription.Id));
    }

    [Fact]
    public async Task GetPlansAsync_throws_configuration_error_when_not_configured()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(HttpStatusCode.OK, "[]"));
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://test.chargify.com/") };
        var service = new MaxioBillingService(client, Options.Create(new MaxioSettings()), NullLogger<MaxioBillingService>.Instance);

        await Assert.ThrowsAsync<MaxioConfigurationException>(() => service.GetPlansAsync());
    }
}
