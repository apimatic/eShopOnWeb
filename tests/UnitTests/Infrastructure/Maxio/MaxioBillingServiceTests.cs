using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioBillingServiceTests
{
    private static readonly MaxioSettings Settings = new()
    {
        ApiKey = "test-key",
        Subdomain = "test-site",
        ProductFamilyHandle = "eshop-subscribe",
        PaymentCollectionMethod = "remittance"
    };

    private static readonly BillingSubscriber Subscriber =
        new(userId: "user-123", email: "demo@example.com");

    private static MaxioBillingService CreateService(FakeMaxioHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = Settings.ResolveBaseAddress() };
        return new MaxioBillingService(http, Settings, NullLogger<MaxioBillingService>.Instance);
    }

    private const string ProductsJson = """
    [
      { "product": { "id": 1, "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900, "interval": 1, "interval_unit": "month", "require_credit_card": false, "archived_at": null } },
      { "product": { "id": 2, "name": "Basic Plan", "handle": "basic-plan", "price_in_cents": 2900, "interval": 1, "interval_unit": "month", "require_credit_card": false, "archived_at": null } },
      { "product": { "id": 3, "name": "Retired Plan", "handle": "old-plan", "price_in_cents": 100, "interval": 1, "interval_unit": "month", "archived_at": "2024-01-01T00:00:00Z" } }
    ]
    """;

    [Fact]
    public async Task GetPlansAsync_MapsActivePlans_AndExcludesArchived()
    {
        var handler = new FakeMaxioHandler((_, path) =>
            path.StartsWith("product_families/handle:eshop-subscribe/products.json")
                ? (HttpStatusCode.OK, ProductsJson)
                : (HttpStatusCode.NotFound, "{}"));

        var plans = await CreateService(handler).GetPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.DoesNotContain(plans, p => p.Handle == "old-plan");
        var pro = plans.Single(p => p.Handle == "eshop-pro");
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal(299m, pro.Price);
        Assert.Equal("month", pro.IntervalUnit);
    }

    [Fact]
    public async Task SubscribeAsync_UnknownPlan_ThrowsNotFound()
    {
        var handler = new FakeMaxioHandler((_, _) => (HttpStatusCode.OK, ProductsJson));

        var ex = await Assert.ThrowsAsync<MaxioBillingException>(
            () => CreateService(handler).SubscribeAsync(Subscriber, "no-such-plan"));

        Assert.Equal(MaxioBillingErrorKind.NotFound, ex.Kind);
    }

    [Fact]
    public async Task SubscribeAsync_NewCustomer_CreatesCustomerAndSubscription_WithRemittance()
    {
        var handler = new FakeMaxioHandler((request, path) =>
        {
            if (path.StartsWith("product_families")) return (HttpStatusCode.OK, ProductsJson);
            if (path.StartsWith("customers/lookup.json")) return (HttpStatusCode.NotFound, "{}");
            if (path == "customers.json" && request.Method == HttpMethod.Post)
                return (HttpStatusCode.Created, """{ "customer": { "id": 555, "reference": "user-123" } }""");
            if (path == "customers/555/subscriptions.json") return (HttpStatusCode.OK, "[]");
            if (path == "subscriptions.json" && request.Method == HttpMethod.Post)
                return (HttpStatusCode.Created, """
                { "subscription": { "id": 9001, "state": "active", "current_period_ends_at": "2026-10-10T00:00:00Z",
                  "product": { "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900, "interval": 1, "interval_unit": "month" } } }
                """);
            return (HttpStatusCode.NotFound, "{}");
        });

        var result = await CreateService(handler).SubscribeAsync(Subscriber, "eshop-pro");

        Assert.False(result.AlreadyExisted);
        Assert.Equal(9001, result.Id);
        Assert.Equal("active", result.State);
        Assert.Equal("eshop-pro", result.PlanHandle);
        Assert.Equal(new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero), result.NextBillingAt);

        // A customer was created and the subscription used the configured collection method + customer id.
        Assert.Contains(handler.Requests, r => r.PathAndQuery == "customers.json" && r.Method == HttpMethod.Post);
        var subRequest = handler.Requests.Single(r => r.PathAndQuery == "subscriptions.json");
        Assert.Contains("\"payment_collection_method\":\"remittance\"", subRequest.Body);
        Assert.Contains("\"customer_id\":555", subRequest.Body);
        Assert.Contains("\"uniqueness_token\"", subRequest.Body);
    }

    [Fact]
    public async Task SubscribeAsync_ExistingLiveSubscription_IsIdempotent_AndDoesNotCreate()
    {
        var handler = new FakeMaxioHandler((request, path) =>
        {
            if (path.StartsWith("product_families")) return (HttpStatusCode.OK, ProductsJson);
            if (path.StartsWith("customers/lookup.json"))
                return (HttpStatusCode.OK, """{ "customer": { "id": 555, "reference": "user-123" } }""");
            if (path == "customers/555/subscriptions.json")
                return (HttpStatusCode.OK, """
                [ { "subscription": { "id": 7777, "state": "active",
                    "product": { "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900, "interval": 1, "interval_unit": "month" } } } ]
                """);
            return (HttpStatusCode.InternalServerError, "{}"); // POST subscriptions.json must NOT be called
        });

        var result = await CreateService(handler).SubscribeAsync(Subscriber, "eshop-pro");

        Assert.True(result.AlreadyExisted);
        Assert.Equal(7777, result.Id);
        Assert.DoesNotContain(handler.Requests, r => r.PathAndQuery == "subscriptions.json");
    }

    [Fact]
    public async Task SubscribeAsync_ValidationError_SurfacesMaxioMessages()
    {
        var handler = new FakeMaxioHandler((request, path) =>
        {
            if (path.StartsWith("product_families")) return (HttpStatusCode.OK, ProductsJson);
            if (path.StartsWith("customers/lookup.json"))
                return (HttpStatusCode.OK, """{ "customer": { "id": 555 } }""");
            if (path == "customers/555/subscriptions.json") return (HttpStatusCode.OK, "[]");
            if (path == "subscriptions.json")
                return ((HttpStatusCode)422, """{ "errors": ["Product can't be blank"] }""");
            return (HttpStatusCode.NotFound, "{}");
        });

        var ex = await Assert.ThrowsAsync<MaxioBillingException>(
            () => CreateService(handler).SubscribeAsync(Subscriber, "eshop-pro"));

        Assert.Equal(MaxioBillingErrorKind.Validation, ex.Kind);
        Assert.Contains("Product can't be blank", ex.CombinedErrors);
    }

    [Fact]
    public async Task GetSubscriptionsAsync_UnknownCustomer_ReturnsEmpty()
    {
        var handler = new FakeMaxioHandler((_, path) =>
            path.StartsWith("customers/lookup.json")
                ? (HttpStatusCode.NotFound, "{}")
                : (HttpStatusCode.InternalServerError, "{}"));

        var result = await CreateService(handler).GetSubscriptionsAsync(Subscriber);

        Assert.Empty(result);
    }
}
