using System.Net;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioBillingServiceTests
{
    private const string BuyerId = "shopper@example.com";
    private const string FamilyHandle = "eshop-subscribe";

    private static MaxioBillingService CreateService(FakeMaxioHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://test.chargify.com/") },
            Options.Create(new MaxioOptions { ApiKey = "test-key", Subdomain = "test", ProductFamilyHandle = FamilyHandle }),
            new KeyedAsyncLock(),
            NullLogger<MaxioBillingService>.Instance);

    [Fact]
    public async Task GetAvailablePlansAsync_ReturnsOnlyUnarchivedPlans_ForConfiguredFamily()
    {
        var handler = new FakeMaxioHandler()
            .On(HttpMethod.Get, "product_families.json", FakeMaxioHandler.JsonResponse(HttpStatusCode.OK,
                """[{"product_family":{"id":1,"name":"Other","handle":"other-family"}},{"product_family":{"id":3023074,"name":"eShop Subscribe","handle":"eshop-subscribe"}}]"""))
            .On(HttpMethod.Get, "product_families/3023074/products.json", FakeMaxioHandler.JsonResponse(HttpStatusCode.OK,
                """
                [
                  {"product":{"id":7126957,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false}},
                  {"product":{"id":9,"name":"Old Plan","handle":"old-plan","price_in_cents":100,"interval":1,"interval_unit":"month","require_credit_card":false,"archived_at":"2020-01-01T00:00:00Z"}}
                ]
                """));

        var sut = CreateService(handler);

        var plans = await sut.GetAvailablePlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal("Pro Plan", plan.Name);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.False(plan.RequiresPaymentMethod);
    }

    [Fact]
    public async Task GetAvailablePlansAsync_Throws_WhenConfiguredFamilyHandleDoesNotExist()
    {
        var handler = new FakeMaxioHandler()
            .On(HttpMethod.Get, "product_families.json", FakeMaxioHandler.JsonResponse(HttpStatusCode.OK, "[]"));

        var sut = CreateService(handler);

        await Assert.ThrowsAsync<MaxioApiException>(() => sut.GetAvailablePlansAsync());
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerThenSubscription_WhenBuyerIsNew()
    {
        var handler = new FakeMaxioHandler()
            .On(HttpMethod.Get, $"customers/lookup.json?reference={Uri.EscapeDataString(BuyerId)}",
                FakeMaxioHandler.JsonResponse(HttpStatusCode.NotFound, """{"errors":["not found"]}"""))
            .On(HttpMethod.Post, "customers.json",
                FakeMaxioHandler.JsonResponse(HttpStatusCode.Created, """{"customer":{"id":501,"first_name":"shopper","last_name":"eShopOnWeb Customer","email":"shopper@example.com","reference":"shopper@example.com"}}"""))
            .On(HttpMethod.Get, "customers/501/subscriptions.json",
                FakeMaxioHandler.JsonResponse(HttpStatusCode.OK, "[]"))
            .On(HttpMethod.Post, "subscriptions.json",
                FakeMaxioHandler.JsonResponse(HttpStatusCode.Created,
                    """{"subscription":{"id":8001,"state":"active","product_price_in_cents":29900,"next_assessment_at":"2026-10-05T00:00:00Z","product":{"id":7126957,"name":"Pro Plan","handle":"eshop-pro"}}}"""));

        var sut = CreateService(handler);

        var result = await sut.SubscribeAsync(BuyerId, BuyerId, "eshop-pro");

        Assert.Equal(8001, result.MaxioSubscriptionId);
        Assert.Equal(501, result.MaxioCustomerId);
        Assert.Equal("active", result.State);
        Assert.Equal(29900, result.PriceInCents);
        Assert.Equal("eshop-pro", result.ProductHandle);
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Post && r.RequestUri!.PathAndQuery == "/subscriptions.json");
    }

    [Fact]
    public async Task SubscribeAsync_ReturnsExistingLiveSubscription_InsteadOfCreatingDuplicate()
    {
        var handler = new FakeMaxioHandler()
            .On(HttpMethod.Get, $"customers/lookup.json?reference={Uri.EscapeDataString(BuyerId)}",
                FakeMaxioHandler.JsonResponse(HttpStatusCode.OK, """{"customer":{"id":501,"reference":"shopper@example.com"}}"""))
            .On(HttpMethod.Get, "customers/501/subscriptions.json",
                FakeMaxioHandler.JsonResponse(HttpStatusCode.OK,
                    """[{"subscription":{"id":8001,"state":"active","product_price_in_cents":29900,"product":{"id":7126957,"name":"Pro Plan","handle":"eshop-pro"}}}]"""));
        // Deliberately no route for POST subscriptions.json - if the service tries to create
        // a second subscription, the fake handler throws and the test fails.

        var sut = CreateService(handler);

        var result = await sut.SubscribeAsync(BuyerId, BuyerId, "eshop-pro");

        Assert.Equal(8001, result.MaxioSubscriptionId);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post && r.RequestUri!.PathAndQuery == "/subscriptions.json");
    }

    [Fact]
    public async Task SubscribeAsync_RecoversFromDuplicateReferenceRace_ByReReadingTheCustomer()
    {
        var handler = new FakeMaxioHandler()
            .On(HttpMethod.Get, $"customers/lookup.json?reference={Uri.EscapeDataString(BuyerId)}", request =>
            {
                // First lookup (pre-create) misses; the recovery lookup after the 422 hits.
                var isFirstCall = !handler_HasCreated;
                handler_HasCreated = true;
                return isFirstCall
                    ? FakeMaxioHandler.JsonResponse(HttpStatusCode.NotFound, "{}")
                    : FakeMaxioHandler.JsonResponse(HttpStatusCode.OK, """{"customer":{"id":501,"reference":"shopper@example.com"}}""");
            })
            .On(HttpMethod.Post, "customers.json",
                FakeMaxioHandler.JsonResponse(HttpStatusCode.UnprocessableEntity, """{"errors":["Reference has already been taken"]}"""))
            .On(HttpMethod.Get, "customers/501/subscriptions.json",
                FakeMaxioHandler.JsonResponse(HttpStatusCode.OK, "[]"))
            .On(HttpMethod.Post, "subscriptions.json",
                FakeMaxioHandler.JsonResponse(HttpStatusCode.Created,
                    """{"subscription":{"id":8002,"state":"active","product_price_in_cents":29900,"product":{"id":7126957,"name":"Pro Plan","handle":"eshop-pro"}}}"""));

        var sut = CreateService(handler);

        var result = await sut.SubscribeAsync(BuyerId, BuyerId, "eshop-pro");

        Assert.Equal(501, result.MaxioCustomerId);
        Assert.Equal(8002, result.MaxioSubscriptionId);
    }

    private bool handler_HasCreated;

    [Fact]
    public async Task SubscribeAsync_ThrowsMaxioApiException_WithParsedErrors_OnValidationFailure()
    {
        var handler = new FakeMaxioHandler()
            .On(HttpMethod.Get, $"customers/lookup.json?reference={Uri.EscapeDataString(BuyerId)}",
                FakeMaxioHandler.JsonResponse(HttpStatusCode.OK, """{"customer":{"id":501,"reference":"shopper@example.com"}}"""))
            .On(HttpMethod.Get, "customers/501/subscriptions.json",
                FakeMaxioHandler.JsonResponse(HttpStatusCode.OK, "[]"))
            .On(HttpMethod.Post, "subscriptions.json",
                FakeMaxioHandler.JsonResponse(HttpStatusCode.UnprocessableEntity, """{"errors":["Product handle: not found"]}"""));

        var sut = CreateService(handler);

        var ex = await Assert.ThrowsAsync<MaxioApiException>(() => sut.SubscribeAsync(BuyerId, BuyerId, "nonexistent-plan"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, ex.StatusCode);
        Assert.True(ex.IsClientError);
        Assert.Contains("Product handle: not found", ex.Errors);
    }

    [Fact]
    public async Task GetSubscriptionsForBuyerAsync_ReturnsEmpty_WhenBuyerHasNoMaxioCustomer()
    {
        var handler = new FakeMaxioHandler()
            .On(HttpMethod.Get, $"customers/lookup.json?reference={Uri.EscapeDataString(BuyerId)}",
                FakeMaxioHandler.JsonResponse(HttpStatusCode.NotFound, "{}"));

        var sut = CreateService(handler);

        var result = await sut.GetSubscriptionsForBuyerAsync(BuyerId);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetSubscriptionsForBuyerAsync_MapsEveryReturnedSubscription()
    {
        var handler = new FakeMaxioHandler()
            .On(HttpMethod.Get, $"customers/lookup.json?reference={Uri.EscapeDataString(BuyerId)}",
                FakeMaxioHandler.JsonResponse(HttpStatusCode.OK, """{"customer":{"id":501,"reference":"shopper@example.com"}}"""))
            .On(HttpMethod.Get, "customers/501/subscriptions.json",
                FakeMaxioHandler.JsonResponse(HttpStatusCode.OK,
                    """
                    [
                      {"subscription":{"id":8001,"state":"active","product_price_in_cents":29900,"product":{"id":7126957,"name":"Pro Plan","handle":"eshop-pro"}}},
                      {"subscription":{"id":8002,"state":"canceled","product_price_in_cents":2900,"product":{"id":7126958,"name":"Basic Plan","handle":"basic-plan"}}}
                    ]
                    """));

        var sut = CreateService(handler);

        var result = await sut.GetSubscriptionsForBuyerAsync(BuyerId);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, s => s.ProductHandle == "eshop-pro" && s.State == "active");
        Assert.Contains(result, s => s.ProductHandle == "basic-plan" && s.State == "canceled");
    }
}
