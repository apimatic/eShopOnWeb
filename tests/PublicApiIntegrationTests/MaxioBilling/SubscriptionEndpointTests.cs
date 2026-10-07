using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.PublicApi.MaxioBilling;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.MaxioBilling;

/// <summary>
/// End-to-end tests of the subscription endpoints through the PublicApi host
/// (in-memory identity DB, real JWT auth, Maxio stubbed at the HttpClient).
/// </summary>
[TestClass]
public class SubscriptionEndpointTests
{
    private static WebApplicationFactory<Program> _factory = null!;
    private static FakeMaxioHandler _maxio = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        _maxio = new FakeMaxioHandler();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Maxio:ApiKey", "test-api-key");
            builder.UseSetting("Maxio:Subdomain", "test-site");
            builder.UseSetting("Maxio:ProductFamilyHandle", "eshop-subscribe");
            builder.ConfigureTestServices(services =>
            {
                // Replace the Maxio primary handler with the shared stub.
                services.AddHttpClient(MaxioBillingService.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => _maxio);
            });
        });
    }

    private static HttpClient NewClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        return client;
    }

    [TestMethod]
    public async Task GetPlans_ReturnsPlansFromConfiguredFamily()
    {
        var client = NewClient();

        var response = await client.GetAsync("/api/subscription-plans");
        var body = await response.Content.ReadFromJsonAsync<ListSubscriptionPlansResponse>();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(1, body!.SubscriptionPlans.Count);
        Assert.AreEqual("eshop-pro", body.SubscriptionPlans[0].Handle);
        Assert.AreEqual(299m, body.SubscriptionPlans[0].Price);
    }

    [TestMethod]
    public async Task GetPlans_WithoutToken_IsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task PostSubscriptions_CreatesSubscription_AndIsIdempotent()
    {
        var client = NewClient();

        var first = await client.PostAsJsonAsync("/api/subscriptions", new CreateSubscriptionRequest { PlanHandle = "eshop-pro" });
        var firstBody = await first.Content.ReadFromJsonAsync<CreateSubscriptionResponse>();

        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode);
        Assert.IsNotNull(firstBody?.Subscription);
        Assert.AreEqual(9001, firstBody.Subscription.SubscriptionId);
        Assert.AreEqual("active", firstBody.Subscription.State);
        Assert.AreEqual(29900, firstBody.Subscription.PriceInCents);
        Assert.IsNotNull(firstBody.Subscription.NextBillingDate);

        // The double-click: same result, no second write.
        var second = await client.PostAsJsonAsync("/api/subscriptions", new CreateSubscriptionRequest { PlanHandle = "eshop-pro" });
        var secondBody = await second.Content.ReadFromJsonAsync<CreateSubscriptionResponse>();

        Assert.AreEqual(HttpStatusCode.Created, second.StatusCode);
        Assert.AreEqual(firstBody.Subscription.SubscriptionId, secondBody!.Subscription!.SubscriptionId);
        Assert.AreEqual(1, _maxio.SubscriptionPosts);
        Assert.AreEqual(1, _maxio.CustomerPosts);
    }

    [TestMethod]
    public async Task PostSubscriptions_WithUnknownPlan_Returns404()
    {
        var client = NewClient();

        var response = await client.PostAsJsonAsync("/api/subscriptions", new CreateSubscriptionRequest { PlanHandle = "does-not-exist" });
        var content = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        StringAssert.Contains(content, "No subscription plan");
    }

    [TestMethod]
    public async Task GetMySubscriptions_ReturnsTheShoppersSubscriptions()
    {
        var client = NewClient();
        _maxio.CustomerExists = true;

        var response = await client.GetAsync("/api/my-subscriptions");
        var body = await response.Content.ReadFromJsonAsync<ListMySubscriptionsResponse>();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(body!.Subscriptions.Any(s => s.SubscriptionId == 9001));
    }
}
