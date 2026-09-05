using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class SubscribeAsyncTests
{
    private const string FamilyResponse = """{"product_family":{"id":1,"name":"eShop Subscribe","handle":"eshop-subscribe"}}""";
    private const string ProductsResponse = """
        [{"product":{"id":10,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month","archived_at":null,"product_family":{"id":1,"handle":"eshop-subscribe"}}}]
        """;

    [Fact]
    public async Task CreatesACustomerAndASubscription_WhenNeitherExistsYet()
    {
        var handler = new FakeMaxioHandler()
            .When(HttpMethod.Get, "subscriptions/lookup.json", _ => FakeMaxioHandler.NotFound())
            .When(HttpMethod.Get, "product_families/handle:eshop-subscribe.json", _ => FakeMaxioHandler.Json(HttpStatusCode.OK, FamilyResponse))
            .When(HttpMethod.Get, "products.json", _ => FakeMaxioHandler.Json(HttpStatusCode.OK, ProductsResponse))
            .When(HttpMethod.Get, "customers/lookup.json", _ => FakeMaxioHandler.NotFound())
            .When(HttpMethod.Post, "customers.json", _ => FakeMaxioHandler.Json(HttpStatusCode.OK,
                """{"customer":{"id":501,"reference":"shopper@example.com","first_name":"Shopper","last_name":"Customer","email":"shopper@example.com"}}"""))
            .When(HttpMethod.Post, "subscriptions.json", _ => FakeMaxioHandler.Json(HttpStatusCode.Created,
                """
                {"subscription":{"id":9001,"state":"active","reference":"eshoponweb:shopper@example.com:eshop-pro",
                  "current_period_ends_at":"2026-10-05T00:00:00Z","next_assessment_at":"2026-10-05T00:00:00Z","created_at":"2026-09-05T00:00:00Z",
                  "product":{"id":10,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month"}}}
                """));

        var service = MaxioTestFactory.CreateService(handler);

        var result = await service.SubscribeAsync("shopper@example.com", "shopper@example.com", "eshop-pro");

        Assert.Equal(9001, result.SubscriptionId);
        Assert.Equal("active", result.State);
        Assert.Equal("eshop-pro", result.PlanHandle);
        Assert.Equal(29900, result.PriceInCents);

        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Post && r.PathAndQuery.StartsWith("/customers.json"));
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Post && r.PathAndQuery.StartsWith("/subscriptions.json"));
    }

    [Fact]
    public async Task IsIdempotent_ReturnsTheExistingSubscriptionWithoutCreatingAnything()
    {
        var handler = new FakeMaxioHandler()
            .When(HttpMethod.Get, "subscriptions/lookup.json", _ => FakeMaxioHandler.Json(HttpStatusCode.OK,
                """
                {"subscription":{"id":9001,"state":"active","reference":"eshoponweb:shopper@example.com:eshop-pro",
                  "current_period_ends_at":"2026-10-05T00:00:00Z","next_assessment_at":"2026-10-05T00:00:00Z","created_at":"2026-09-05T00:00:00Z",
                  "product":{"id":10,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month"}}}
                """));

        var service = MaxioTestFactory.CreateService(handler);

        var first = await service.SubscribeAsync("shopper@example.com", "shopper@example.com", "eshop-pro");
        var second = await service.SubscribeAsync("shopper@example.com", "shopper@example.com", "eshop-pro");

        Assert.Equal(9001, first.SubscriptionId);
        Assert.Equal(9001, second.SubscriptionId);

        // Only the reference lookup should ever be called - no customer or subscription creation.
        Assert.All(handler.Requests, r => Assert.Equal(HttpMethod.Get, r.Method));
        Assert.All(handler.Requests, r => Assert.StartsWith("/subscriptions/lookup.json", r.PathAndQuery));
    }

    [Fact]
    public async Task ReusesAnExistingCustomer_InsteadOfCreatingADuplicate()
    {
        var handler = new FakeMaxioHandler()
            .When(HttpMethod.Get, "subscriptions/lookup.json", _ => FakeMaxioHandler.NotFound())
            .When(HttpMethod.Get, "product_families/handle:eshop-subscribe.json", _ => FakeMaxioHandler.Json(HttpStatusCode.OK, FamilyResponse))
            .When(HttpMethod.Get, "products.json", _ => FakeMaxioHandler.Json(HttpStatusCode.OK, ProductsResponse))
            .When(HttpMethod.Get, "customers/lookup.json", _ => FakeMaxioHandler.Json(HttpStatusCode.OK,
                """{"customer":{"id":501,"reference":"shopper@example.com","first_name":"Shopper","last_name":"Customer","email":"shopper@example.com"}}"""))
            .When(HttpMethod.Post, "subscriptions.json", _ => FakeMaxioHandler.Json(HttpStatusCode.Created,
                """
                {"subscription":{"id":9002,"state":"active","reference":"eshoponweb:shopper@example.com:eshop-pro",
                  "current_period_ends_at":"2026-10-05T00:00:00Z","next_assessment_at":"2026-10-05T00:00:00Z","created_at":"2026-09-05T00:00:00Z",
                  "product":{"id":10,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month"}}}
                """));
        // Note: no "POST customers.json" route registered - the test fails if the service tries to create one.

        var service = MaxioTestFactory.CreateService(handler);

        var result = await service.SubscribeAsync("shopper@example.com", "shopper@example.com", "eshop-pro");

        Assert.Equal(9002, result.SubscriptionId);
    }

    [Fact]
    public async Task ThrowsSubscriptionPlanNotFoundException_ForAnUnknownPlanHandle()
    {
        var handler = new FakeMaxioHandler()
            .When(HttpMethod.Get, "subscriptions/lookup.json", _ => FakeMaxioHandler.NotFound())
            .When(HttpMethod.Get, "product_families/handle:eshop-subscribe.json", _ => FakeMaxioHandler.Json(HttpStatusCode.OK, FamilyResponse))
            .When(HttpMethod.Get, "products.json", _ => FakeMaxioHandler.Json(HttpStatusCode.OK, ProductsResponse));

        var service = MaxioTestFactory.CreateService(handler);

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => service.SubscribeAsync("shopper@example.com", "shopper@example.com", "does-not-exist"));

        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post);
    }
}
