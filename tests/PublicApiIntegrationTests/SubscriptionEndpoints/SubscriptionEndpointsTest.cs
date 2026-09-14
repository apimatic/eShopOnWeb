using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PublicApiIntegrationTests.Maxio;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTest
{
    private StubMaxioHandler _stub = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    [TestInitialize]
    public void Initialize()
    {
        _stub = new StubMaxioHandler();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Maxio:ApiKey", "stub-key");
            builder.UseSetting("Maxio:Subdomain", "stub-site");
            builder.UseSetting("Maxio:ProductFamilyHandle", StubMaxioHandler.FamilyHandle);
            builder.ConfigureServices(services =>
            {
                services.AddHttpClient(MaxioServiceCollectionExtensions.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => _stub);
            });
        });
        _client = _factory.CreateClient();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [TestMethod]
    public async Task Plans_WithoutToken_Returns401()
    {
        var response = await _client.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Subscribe_WithoutToken_Returns401()
    {
        var response = await _client.PostAsync("api/subscriptions",
            new StringContent("""{"productHandle":"test-pro"}""", Encoding.UTF8, "application/json"));

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Subscribe_WithToken_ReturnsConfirmationWithPlanPriceStateAndNextBillingDate()
    {
        Authorize("demouser@microsoft.com");

        var response = await _client.PostAsync("api/subscriptions",
            new StringContent("""{"productHandle":"test-pro"}""", Encoding.UTF8, "application/json"));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var subscription = doc.RootElement.GetProperty("subscription");
        Assert.AreEqual("active", subscription.GetProperty("state").GetString());
        Assert.AreEqual("test-pro", subscription.GetProperty("planHandle").GetString());
        Assert.AreEqual("Test Pro", subscription.GetProperty("planName").GetString());
        Assert.AreEqual(299m, subscription.GetProperty("price").GetDecimal());
        Assert.IsTrue(subscription.TryGetProperty("nextBillingDateUtc", out var nextBilling) && nextBilling.ValueKind != JsonValueKind.Null);
        Assert.AreEqual(1, _stub.CustomerCreates);
        Assert.AreEqual(1, _stub.SubscriptionCreates);
    }

    [TestMethod]
    public async Task Subscribe_Twice_IsIdempotent()
    {
        Authorize("demouser@microsoft.com");
        var content = new StringContent("""{"productHandle":"test-pro"}""", Encoding.UTF8, "application/json");

        var first = await _client.PostAsync("api/subscriptions", content);
        var second = await _client.PostAsync("api/subscriptions", content);

        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
        using var firstDoc = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        using var secondDoc = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.AreEqual(
            firstDoc.RootElement.GetProperty("subscription").GetProperty("subscriptionId").GetInt32(),
            secondDoc.RootElement.GetProperty("subscription").GetProperty("subscriptionId").GetInt32());
        Assert.AreEqual(1, _stub.SubscriptionCreates);
    }

    [TestMethod]
    public async Task Subscribe_WithEmptyProductHandle_Returns400()
    {
        Authorize("demouser@microsoft.com");

        var response = await _client.PostAsync("api/subscriptions",
            new StringContent("""{"productHandle":""}""", Encoding.UTF8, "application/json"));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task Plans_WithToken_ReturnsConfiguredFamilyPlans()
    {
        Authorize("demouser@microsoft.com");

        var response = await _client.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var plans = doc.RootElement.GetProperty("subscriptionPlans");
        Assert.AreEqual(2, plans.GetArrayLength());
        Assert.IsTrue(plans.EnumerateArray().Any(p => p.GetProperty("handle").GetString() == StubMaxioHandler.ProHandle));
    }

    [TestMethod]
    public async Task MySubscriptions_WithToken_ReturnsSubscriptionsAfterSubscribing()
    {
        Authorize("demouser@microsoft.com");
        await _client.PostAsync("api/subscriptions",
            new StringContent("""{"productHandle":"test-pro"}""", Encoding.UTF8, "application/json"));

        var response = await _client.GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var subscriptions = doc.RootElement.GetProperty("subscriptions");
        Assert.AreEqual(1, subscriptions.GetArrayLength());
        Assert.AreEqual("test-pro", subscriptions[0].GetProperty("planHandle").GetString());
        Assert.AreEqual("active", subscriptions[0].GetProperty("state").GetString());
    }

    private void Authorize(string userName)
    {
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.CreateTestToken(userName));
    }
}
