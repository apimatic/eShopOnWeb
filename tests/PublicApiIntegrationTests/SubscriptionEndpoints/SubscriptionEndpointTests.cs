using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointTests
{
    private static WebApplicationFactory<Program>? _factory;
    private static readonly StubMaxioHandler _stub = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [TestInitialize]
    public void ResetStub()
    {
        _stub.Requests.Clear();
        _stub.CustomerExists = false;
        _stub.SubscriptionExists = false;
    }

    [TestMethod]
    public async Task PlansRequireAuthentication()
    {
        var client = GetClient();
        var response = await client.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task PlansAreListedFromTheBillingSystem()
    {
        var client = GetAuthenticatedClient();
        var response = await client.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJson<JsonElement>(response);
        var plans = body.GetProperty("plans");
        Assert.AreEqual(2, plans.GetArrayLength());

        var pro = plans[0];
        Assert.AreEqual(StubMaxioHandler.ProHandle, pro.GetProperty("handle").GetString());
        Assert.AreEqual(StubMaxioHandler.ProPriceCents, pro.GetProperty("priceInCents").GetInt64());
        Assert.AreEqual(299.00m, pro.GetProperty("price").GetDecimal());
        Assert.AreEqual("month", pro.GetProperty("intervalUnit").GetString());
        Assert.AreEqual(1, pro.GetProperty("interval").GetInt32());
    }

    [TestMethod]
    public async Task SubscribeEnrollsTheUserAndConfirmsPlanStateAndNextBilling()
    {
        var client = GetAuthenticatedClient();
        var response = await client.PostAsync("api/subscriptions", JsonBody(new { planHandle = StubMaxioHandler.ProHandle }));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        var subscription = (await ReadJson<JsonElement>(response)).GetProperty("subscription");
        Assert.AreEqual(StubMaxioHandler.ProHandle, subscription.GetProperty("planHandle").GetString());
        Assert.AreEqual("eShop Pro", subscription.GetProperty("planName").GetString());
        Assert.AreEqual("active", subscription.GetProperty("state").GetString());
        Assert.AreEqual(299.00m, subscription.GetProperty("productPrice").GetDecimal());
        Assert.IsTrue(subscription.TryGetProperty("nextBillingDate", out var nextBilling) && nextBilling.ValueKind != JsonValueKind.Null);
        Assert.AreEqual(StubMaxioHandler.SubscriptionId, subscription.GetProperty("subscriptionId").GetInt32());
        Assert.AreEqual(1, _stub.CustomerCreateCount);
        Assert.AreEqual(1, _stub.SubscriptionCreateCount);
    }

    [TestMethod]
    public async Task SubscribeIsIdempotentAgainstDoubleClicks()
    {
        var client = GetAuthenticatedClient();
        var first = await client.PostAsync("api/subscriptions", JsonBody(new { planHandle = StubMaxioHandler.ProHandle }));
        var second = await client.PostAsync("api/subscriptions", JsonBody(new { planHandle = StubMaxioHandler.ProHandle }));

        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);

        var firstId = (await ReadJson<JsonElement>(first)).GetProperty("subscription").GetProperty("subscriptionId").GetInt32();
        var secondId = (await ReadJson<JsonElement>(second)).GetProperty("subscription").GetProperty("subscriptionId").GetInt32();
        Assert.AreEqual(firstId, secondId);

        // One customer and one subscription on the provider, however many clicks.
        Assert.AreEqual(1, _stub.CustomerCreateCount);
        Assert.AreEqual(1, _stub.SubscriptionCreateCount);
    }

    [TestMethod]
    public async Task MySubscriptionsReflectEnrollment()
    {
        var client = GetAuthenticatedClient();
        await client.PostAsync("api/subscriptions", JsonBody(new { planHandle = StubMaxioHandler.ProHandle }));

        var response = await client.GetAsync("api/my-subscriptions");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var subscriptions = (await ReadJson<JsonElement>(response)).GetProperty("subscriptions");
        Assert.AreEqual(1, subscriptions.GetArrayLength());
        Assert.AreEqual(StubMaxioHandler.SubscriptionId, subscriptions[0].GetProperty("subscriptionId").GetInt32());
        Assert.AreEqual(StubMaxioHandler.ProHandle, subscriptions[0].GetProperty("planHandle").GetString());
        Assert.AreEqual("active", subscriptions[0].GetProperty("state").GetString());
    }

    [TestMethod]
    public async Task MySubscriptionsAreEmptyBeforeEnrollment()
    {
        var client = GetAuthenticatedClient();
        var response = await client.GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(0, (await ReadJson<JsonElement>(response)).GetProperty("subscriptions").GetArrayLength());
        Assert.AreEqual(0, _stub.SubscriptionCreateCount);
    }

    [TestMethod]
    public async Task SubscribeWithUnknownPlanReturnsNotFound()
    {
        var client = GetAuthenticatedClient();
        var response = await client.PostAsync("api/subscriptions", JsonBody(new { planHandle = "ghost-plan" }));

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.AreEqual(0, _stub.SubscriptionCreateCount);
    }

    [TestMethod]
    public async Task SubscribeWithoutPlanHandleReturnsBadRequest()
    {
        var client = GetAuthenticatedClient();
        var response = await client.PostAsync("api/subscriptions", JsonBody(new { }));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static HttpClient GetClient()
    {
        if (_factory is null)
        {
            _factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseSetting("Maxio:ApiKey", "test-key");
                    builder.UseSetting("Maxio:Subdomain", "test-site");
                    builder.UseSetting("Maxio:ProductFamilyHandle", StubMaxioHandler.FamilyHandle);
                    builder.ConfigureServices(services =>
                    {
                        // Replace the primary handler of the named client the
                        // SDK resolves, so no request reaches the network.
                        services.AddHttpClient(MaxioDependencies.HttpClientName)
                            .ConfigurePrimaryHttpMessageHandler(() => _stub);
                    });
                });
        }
        return _factory.CreateClient();
    }

    private static HttpClient GetAuthenticatedClient()
    {
        var client = GetClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        return client;
    }

    private static StringContent JsonBody(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private static async Task<JsonElement> ReadJson<T>(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(text, JsonOptions);
    }
}
