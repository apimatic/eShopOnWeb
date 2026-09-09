using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Exercises the subscription endpoints through the full PublicApi pipeline with the
/// Maxio SDK client replaced by a stub-backed instance (no real network traffic).
/// </summary>
[TestClass]
public class SubscriptionEndpointsTest
{
    private const string FamiliesJson =
        """[{"product_family":{"id":1,"handle":"eshop-subscribe","name":"eShop Subscribe"}}]""";

    private const string ProductsJson =
        """[{"product":{"id":10,"handle":"eshop-pro","name":"Pro Plan","description":"The pro plan","price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false}},{"product":{"id":11,"handle":"basic-plan","name":"Basic Plan","price_in_cents":2900,"interval":1,"interval_unit":"month","require_credit_card":false}}]""";

    private const string CustomerJson =
        """{"customer":{"id":55,"first_name":"Demo","last_name":"User","email":"demouser@microsoft.com","reference":"3a1b2c3d-0000-0000-0000-000000000000"}}""";

    private const string CreatedSubscriptionJson =
        """{"subscription":{"id":777,"state":"active","product_id":10,"product_price_in_cents":29900,"next_assessment_at":"2026-10-09T00:00:00Z","current_period_ends_at":"2026-10-09T00:00:00Z","current_period_started_at":"2026-09-09T00:00:00Z","product":{"id":10,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month"}}}""";

    private static WebApplicationFactory<Program> CreateFactory(StubMaxioHandler handler)
    {
        var stubClient = new MaxioAdvancedBillingClient(new HttpClient(handler), new MaxioAdvancedBillingClientOptions());
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<MaxioAdvancedBillingClient>();
                    services.AddSingleton(stubClient);
                });
            });
    }

    private static HttpClient NewAuthorizedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        return client;
    }

    [TestMethod]
    public async Task ListPlans_ReturnsUnauthorizedWithoutToken()
    {
        using var factory = CreateFactory(new StubMaxioHandler());
        var client = factory.CreateClient();

        var response = await client.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListPlans_ReturnsPlansForAuthenticatedUser()
    {
        var handler = new StubMaxioHandler { FamiliesJson = FamiliesJson, ProductsJson = ProductsJson };
        using var factory = CreateFactory(handler);
        var client = NewAuthorizedClient(factory);

        var response = await client.GetAsync("api/subscription-plans");

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var plans = document.RootElement.GetProperty("subscriptionPlans");
        Assert.AreEqual(2, plans.GetArrayLength());
        var pro = plans.EnumerateArray().Single(p => p.GetProperty("handle").GetString() == "eshop-pro");
        Assert.AreEqual("Pro Plan", pro.GetProperty("name").GetString());
        Assert.AreEqual(299.00m, pro.GetProperty("price").GetDecimal());
    }

    [TestMethod]
    public async Task Subscribe_CreatesSubscriptionAndConfirmsPlanPriceStateAndNextBilling()
    {
        var handler = new StubMaxioHandler
        {
            FamiliesJson = FamiliesJson,
            ProductsJson = ProductsJson,
            CustomerExists = true,
            CustomerSubscriptionsJson = "[]"
        };
        using var factory = CreateFactory(handler);
        var client = NewAuthorizedClient(factory);

        var response = await client.PostAsync("api/subscriptions",
            new StringContent("""{"productHandle":"eshop-pro"}""", Encoding.UTF8, "application/json"));

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var subscription = document.RootElement.GetProperty("subscription");
        Assert.AreEqual("eshop-pro", subscription.GetProperty("planHandle").GetString());
        Assert.AreEqual("Pro Plan", subscription.GetProperty("planName").GetString());
        Assert.AreEqual(299.00m, subscription.GetProperty("price").GetDecimal());
        Assert.AreEqual("active", subscription.GetProperty("state").GetString());
        Assert.AreEqual("2026-10-09T00:00:00Z", subscription.GetProperty("nextBillingDate").GetDateTimeOffset().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"));
        Assert.AreEqual(false, subscription.GetProperty("alreadySubscribed").GetBoolean());
        Assert.AreEqual(1, handler.SubscriptionCreateCount);
    }

    [TestMethod]
    public async Task Subscribe_MissingPlanHandle_ReturnsValidationProblem()
    {
        var handler = new StubMaxioHandler();
        using var factory = CreateFactory(handler);
        var client = NewAuthorizedClient(factory);

        var response = await client.PostAsync("api/subscriptions",
            new StringContent("""{}""", Encoding.UTF8, "application/json"));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual(0, handler.SubscriptionCreateCount);
    }

    [TestMethod]
    public async Task Subscribe_UnknownPlan_ReturnsNotFound()
    {
        var handler = new StubMaxioHandler
        {
            FamiliesJson = FamiliesJson,
            ProductsJson = ProductsJson,
            CustomerExists = true
        };
        using var factory = CreateFactory(handler);
        var client = NewAuthorizedClient(factory);

        var response = await client.PostAsync("api/subscriptions",
            new StringContent("""{"productHandle":"no-such-plan"}""", Encoding.UTF8, "application/json"));

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.AreEqual(0, handler.SubscriptionCreateCount);
    }

    [TestMethod]
    public async Task MySubscriptions_ReturnsUsersSubscriptions()
    {
        var handler = new StubMaxioHandler
        {
            FamiliesJson = FamiliesJson,
            ProductsJson = ProductsJson,
            CustomerExists = true,
            CustomerSubscriptionsJson = $"[{CreatedSubscriptionJson}]"
        };
        using var factory = CreateFactory(handler);
        var client = NewAuthorizedClient(factory);

        var response = await client.GetAsync("api/my-subscriptions");

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var subscriptions = document.RootElement.GetProperty("subscriptions");
        Assert.AreEqual(1, subscriptions.GetArrayLength());
        var subscription = subscriptions[0];
        Assert.AreEqual(777, subscription.GetProperty("subscriptionId").GetInt32());
        Assert.AreEqual("active", subscription.GetProperty("state").GetString());
    }

    [TestMethod]
    public async Task MySubscriptions_NoBillingProfile_ReturnsEmptyList()
    {
        var handler = new StubMaxioHandler { CustomerExists = false };
        using var factory = CreateFactory(handler);
        var client = NewAuthorizedClient(factory);

        var response = await client.GetAsync("api/my-subscriptions");

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        Assert.AreEqual(0, document.RootElement.GetProperty("subscriptions").GetArrayLength());
    }
}
