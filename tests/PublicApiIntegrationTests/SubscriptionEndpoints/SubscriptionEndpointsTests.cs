using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Billing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTests
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static async Task AssertStatus(HttpStatusCode expected, HttpResponseMessage response) =>
        Assert.AreEqual(expected, response.StatusCode, await response.Content.ReadAsStringAsync());

    private static Task<HttpResponseMessage> Subscribe(HttpClient client, string planHandle) =>
        client.PostAsJsonAsync("api/subscriptions", new { planHandle });

    [TestMethod]
    public async Task Endpoints_RequireAJwt()
    {
        using var factory = new SubscriptionApiFactory();
        var anonymous = factory.CreateClient();

        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("api/subscription-plans")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await Subscribe(anonymous, "eshop-pro")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("api/my-subscriptions")).StatusCode);
        Assert.AreEqual(0, factory.Maxio.Requests.Count);
    }

    [TestMethod]
    public async Task Plans_ListsActivePlansOfTheConfiguredFamily()
    {
        using var factory = new SubscriptionApiFactory();
        var client = factory.ClientFor(SubscriptionApiFactory.NewUserName());

        var response = await client.GetAsync("api/subscription-plans");

        await AssertStatus(HttpStatusCode.OK, response);
        var json = await ReadJson(response);
        Assert.IsTrue(json.GetProperty("isComplete").GetBoolean());
        var handles = json.GetProperty("plans").EnumerateArray().Select(p => p.GetProperty("handle").GetString()).ToList();
        CollectionAssert.AreEquivalent(new[] { "eshop-pro", "basic-plan" }, handles);   // archived plan excluded
        var pro = json.GetProperty("plans").EnumerateArray().First(p => p.GetProperty("handle").GetString() == "eshop-pro");
        Assert.AreEqual(299m, pro.GetProperty("price").GetDecimal());
        Assert.AreEqual("month", pro.GetProperty("intervalUnit").GetString());
    }

    [TestMethod]
    public async Task Subscribe_CreatesCustomerAndSubscription_AndConfirmsPlanPriceStateAndNextBilling()
    {
        using var factory = new SubscriptionApiFactory();
        var userName = SubscriptionApiFactory.NewUserName();
        var client = factory.ClientFor(userName);

        var response = await Subscribe(client, "eshop-pro");

        await AssertStatus(HttpStatusCode.Created, response);
        var json = await ReadJson(response);
        Assert.AreEqual("created", json.GetProperty("status").GetString());
        var sub = json.GetProperty("subscription");
        Assert.AreEqual("eshop-pro", sub.GetProperty("planHandle").GetString());
        Assert.AreEqual(299m, sub.GetProperty("price").GetDecimal());
        Assert.AreEqual("active", sub.GetProperty("state").GetString());
        Assert.AreNotEqual(JsonValueKind.Null, sub.GetProperty("nextBillingAt").ValueKind);

        Assert.AreEqual(1, factory.Maxio.CustomersByReference.Count);
        Assert.AreEqual(1, factory.Maxio.SubscriptionsByReference.Count);
        var customer = factory.Maxio.CustomersByReference.Values.Single();
        Assert.AreEqual(userName, (string?)customer["email"]);
        Assert.AreEqual(BillingReferences.ForUser(userName), (string?)customer["reference"]);

        var createBody = factory.Maxio.Requests.Single(r => r.Method == HttpMethod.Post && r.Path == "/subscriptions.json").Body!;
        var sent = JsonDocument.Parse(createBody).RootElement.GetProperty("subscription");
        Assert.AreEqual("eshop-pro", sent.GetProperty("product_handle").GetString());
        Assert.AreEqual("remittance", sent.GetProperty("payment_collection_method").GetString());
        Assert.AreEqual(BillingReferences.ForSubscription(BillingReferences.ForUser(userName)), sent.GetProperty("reference").GetString());
    }

    [TestMethod]
    public async Task Subscribe_ConcurrentDoubleClick_CreatesExactlyOneSubscription()
    {
        using var factory = new SubscriptionApiFactory();
        var client = factory.ClientFor(SubscriptionApiFactory.NewUserName());
        factory.Maxio.SubscriptionCreateGate = new TaskCompletionSource();

        var first = Subscribe(client, "eshop-pro");
        // Wait until the first request is parked inside the provider write.
        for (var i = 0; i < 200 && factory.Maxio.Count(HttpMethod.Post, "/subscriptions.json") == 0; i++)
        {
            await Task.Delay(25);
        }
        var second = await Subscribe(client, "eshop-pro");
        factory.Maxio.SubscriptionCreateGate.SetResult();
        var firstResponse = await first;

        await AssertStatus(HttpStatusCode.Created, firstResponse);
        await AssertStatus(HttpStatusCode.Accepted, second);
        Assert.AreEqual("in_progress", (await ReadJson(second)).GetProperty("status").GetString());
        Assert.AreEqual(1, factory.Maxio.Count(HttpMethod.Post, "/subscriptions.json"));
        Assert.AreEqual(1, factory.Maxio.Count(HttpMethod.Post, "/customers.json"));
    }

    [TestMethod]
    public async Task Subscribe_Repeated_ReturnsExisting_AndDifferentPlanConflicts()
    {
        using var factory = new SubscriptionApiFactory();
        var client = factory.ClientFor(SubscriptionApiFactory.NewUserName());

        await AssertStatus(HttpStatusCode.Created, await Subscribe(client, "eshop-pro"));
        var again = await Subscribe(client, "eshop-pro");
        var other = await Subscribe(client, "basic-plan");

        await AssertStatus(HttpStatusCode.OK, again);
        Assert.AreEqual("already_subscribed", (await ReadJson(again)).GetProperty("status").GetString());
        await AssertStatus(HttpStatusCode.Conflict, other);
        Assert.AreEqual("eshop-pro", (await ReadJson(other)).GetProperty("existingSubscription").GetProperty("planHandle").GetString());
        Assert.AreEqual(1, factory.Maxio.Count(HttpMethod.Post, "/subscriptions.json"));
    }

    [TestMethod]
    public async Task Subscribe_PlanNotOffered_IsRejectedBeforeAnyWrite()
    {
        using var factory = new SubscriptionApiFactory();
        var client = factory.ClientFor(SubscriptionApiFactory.NewUserName());

        var unknown = await Subscribe(client, "not-a-plan");
        var archived = await Subscribe(client, "retired-plan");

        await AssertStatus(HttpStatusCode.BadRequest, unknown);
        await AssertStatus(HttpStatusCode.BadRequest, archived);
        Assert.IsFalse(factory.Maxio.Requests.Any(r => r.Method == HttpMethod.Post));
    }

    [TestMethod]
    public async Task CreateCustomerConnectionDrop_IsReconciledByReference()
    {
        using var factory = new SubscriptionApiFactory();
        var client = factory.ClientFor(SubscriptionApiFactory.NewUserName());
        factory.Maxio.DropAfterCustomerCreate = true;

        var response = await Subscribe(client, "eshop-pro");

        await AssertStatus(HttpStatusCode.Created, response);
        Assert.AreEqual(1, factory.Maxio.Count(HttpMethod.Post, "/customers.json"));   // never resent
        Assert.AreEqual(1, factory.Maxio.CustomersByReference.Count);
        Assert.AreEqual(1, factory.Maxio.SubscriptionsByReference.Count);
    }

    [TestMethod]
    public async Task CreateSubscriptionConnectionDrop_IsReconciledByReference()
    {
        using var factory = new SubscriptionApiFactory();
        var client = factory.ClientFor(SubscriptionApiFactory.NewUserName());
        factory.Maxio.DropAfterSubscriptionCreate = true;

        var response = await Subscribe(client, "eshop-pro");

        await AssertStatus(HttpStatusCode.Created, response);
        Assert.AreEqual("created", (await ReadJson(response)).GetProperty("status").GetString());
        Assert.AreEqual(1, factory.Maxio.Count(HttpMethod.Post, "/subscriptions.json"));   // never resent
        Assert.AreEqual(1, factory.Maxio.SubscriptionsByReference.Count);

        // The enrollment is settled: a repeat is recognised without another write.
        var again = await Subscribe(client, "eshop-pro");
        await AssertStatus(HttpStatusCode.OK, again);
        Assert.AreEqual(1, factory.Maxio.Count(HttpMethod.Post, "/subscriptions.json"));
    }

    [TestMethod]
    public async Task ProviderRejection_Returns422_AndReleasesTheClaimForARetry()
    {
        using var factory = new SubscriptionApiFactory();
        var client = factory.ClientFor(SubscriptionApiFactory.NewUserName());
        factory.Maxio.RejectSubscriptionWith = "No payment method was on file";

        var rejected = await Subscribe(client, "eshop-pro");

        await AssertStatus((HttpStatusCode)422, rejected);
        var json = await ReadJson(rejected);
        Assert.AreEqual("No payment method was on file", json.GetProperty("errors")[0].GetString());

        factory.Maxio.RejectSubscriptionWith = null;
        var retried = await Subscribe(client, "eshop-pro");
        await AssertStatus(HttpStatusCode.Created, retried);
        Assert.AreEqual(1, factory.Maxio.CustomersByReference.Count);   // the customer is reused, not recreated
    }

    [TestMethod]
    public async Task Subscribe_AdoptsAnExistingProviderSubscription_WithoutWriting()
    {
        // Simulates a restart: Maxio holds the subscription, the local claim store does not.
        using var factory = new SubscriptionApiFactory();
        var userName = SubscriptionApiFactory.NewUserName();
        var userReference = BillingReferences.ForUser(userName);
        factory.Maxio.SeedSubscription(BillingReferences.ForSubscription(userReference), "eshop-pro", customerId: 42);
        var client = factory.ClientFor(userName);

        var response = await Subscribe(client, "eshop-pro");

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.AreEqual("already_subscribed", (await ReadJson(response)).GetProperty("status").GetString());
        Assert.IsFalse(factory.Maxio.Requests.Any(r => r.Method == HttpMethod.Post));
    }

    [TestMethod]
    public async Task MySubscriptions_ReflectsTheSubscription()
    {
        using var factory = new SubscriptionApiFactory();
        var client = factory.ClientFor(SubscriptionApiFactory.NewUserName());

        var before = await ReadJson(await client.GetAsync("api/my-subscriptions"));
        Assert.AreEqual(0, before.GetProperty("subscriptions").GetArrayLength());

        await Subscribe(client, "basic-plan");
        var after = await client.GetAsync("api/my-subscriptions");

        await AssertStatus(HttpStatusCode.OK, after);
        var subs = (await ReadJson(after)).GetProperty("subscriptions");
        Assert.AreEqual(1, subs.GetArrayLength());
        Assert.AreEqual("basic-plan", subs[0].GetProperty("planHandle").GetString());
        Assert.AreEqual(29m, subs[0].GetProperty("price").GetDecimal());
        Assert.AreEqual("active", subs[0].GetProperty("state").GetString());
    }
}
