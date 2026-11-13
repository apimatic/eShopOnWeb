using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTest
{
    private static readonly HttpMethod Get = HttpMethod.Get;
    private static readonly HttpMethod Post = HttpMethod.Post;

    [TestMethod]
    public async Task PlansRequireAuthentication()
    {
        using var api = new MaxioTestApi();
        var response = await api.CreateClient().GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.AreEqual(0, api.Maxio.Requests.Count);
    }

    [TestMethod]
    public async Task ListsTheConfiguredFamilysActivePlans()
    {
        using var api = new MaxioTestApi();
        var (client, _) = await api.NewShopperAsync();

        var response = await client.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var json = await Json(response);
        var handles = json.GetProperty("plans").EnumerateArray().Select(p => p.GetProperty("handle").GetString()).ToList();
        CollectionAssert.AreEqual(new[] { "test-pro", "test-basic" }, handles); // archived plan excluded
        Assert.AreEqual(299m, json.GetProperty("plans")[0].GetProperty("price").GetDecimal());
        Assert.IsFalse(json.GetProperty("truncated").GetBoolean());
        Assert.IsTrue(api.Maxio.Requests.All(r => r.PathAndQuery.Contains("handle%3Atest-family")));
    }

    [TestMethod]
    public async Task MarksPlanListTruncatedWhenThePageCapIsHit()
    {
        using var api = new MaxioTestApi(new() { ["Maxio:PlansPerPage"] = "1", ["Maxio:MaxPlanPages"] = "1" });
        var (client, _) = await api.NewShopperAsync();

        var json = await Json(await client.GetAsync("api/subscription-plans"));

        Assert.IsTrue(json.GetProperty("truncated").GetBoolean());
        Assert.AreEqual(1, json.GetProperty("plans").GetArrayLength());
    }

    [TestMethod]
    public async Task SubscribeCreatesCustomerAndSubscriptionAndConfirmsIt()
    {
        using var api = new MaxioTestApi();
        var (client, userName) = await api.NewShopperAsync();

        var response = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "test-pro" });

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var json = await Json(response);
        Assert.IsTrue(json.GetProperty("created").GetBoolean());
        var sub = json.GetProperty("subscription");
        Assert.AreEqual("test-pro", sub.GetProperty("planHandle").GetString());
        Assert.AreEqual(299m, sub.GetProperty("price").GetDecimal());
        Assert.AreEqual("active", sub.GetProperty("state").GetString());
        Assert.AreEqual(2026, sub.GetProperty("nextBillingAt").GetDateTimeOffset().Year);

        var customerBody = JsonDocument.Parse(api.Maxio.Bodies(Post, "/customers.json").Single()).RootElement.GetProperty("customer");
        Assert.AreEqual(userName, customerBody.GetProperty("email").GetString());
        StringAssert.StartsWith(customerBody.GetProperty("reference").GetString(), "eshop-");
        Assert.IsFalse(customerBody.GetProperty("reference").GetString()!.Contains('@'), "reference must not carry the e-mail");

        var subscriptionBody = JsonDocument.Parse(api.Maxio.Bodies(Post, "/subscriptions.json").Single()).RootElement.GetProperty("subscription");
        Assert.AreEqual("test-pro", subscriptionBody.GetProperty("product_handle").GetString());
        Assert.AreEqual(1001, subscriptionBody.GetProperty("customer_id").GetInt32());
        Assert.AreEqual("remittance", subscriptionBody.GetProperty("payment_collection_method").GetString());
        StringAssert.StartsWith(subscriptionBody.GetProperty("reference").GetString(), "eshop-sub-");
    }

    [TestMethod]
    public async Task ConcurrentDoubleClickCreatesExactlyOneCustomerAndSubscription()
    {
        using var api = new MaxioTestApi();
        var (client, _) = await api.NewShopperAsync();
        // Create the customer first so both clicks race on the subscription claim itself.
        Assert.AreEqual(HttpStatusCode.Created, (await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "test-basic" })).StatusCode);
        api.Maxio.CreateSubscriptionDelay = System.TimeSpan.FromMilliseconds(700);

        var clicks = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => client.PostAsJsonAsync("api/subscriptions", new { planHandle = "test-pro" })));

        var statuses = clicks.Select(r => r.StatusCode).ToList();
        Assert.AreEqual(1, statuses.Count(s => s == HttpStatusCode.Created), string.Join(",", statuses));
        Assert.IsTrue(statuses.All(s => s is HttpStatusCode.Created or HttpStatusCode.Conflict), string.Join(",", statuses));
        Assert.AreEqual(1, api.Maxio.Count(Post, "/customers.json"));
        Assert.AreEqual(2, api.Maxio.Count(Post, "/subscriptions.json")); // one basic + exactly one pro

        // A later repeat returns the existing subscription instead of creating another.
        api.Maxio.CreateSubscriptionDelay = System.TimeSpan.Zero;
        var repeat = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "test-pro" });
        Assert.AreEqual(HttpStatusCode.OK, repeat.StatusCode);
        Assert.IsFalse((await Json(repeat)).GetProperty("created").GetBoolean());
        Assert.AreEqual(2, api.Maxio.Count(Post, "/subscriptions.json"));
    }

    [TestMethod]
    public async Task RejectsAPlanTheFamilyDoesNotOffer()
    {
        using var api = new MaxioTestApi();
        var (client, _) = await api.NewShopperAsync();

        var unknown = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "not-offered" });
        var archived = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "test-retired" });

        Assert.AreEqual(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, archived.StatusCode);
        Assert.AreEqual(0, api.Maxio.Count(Post, "/customers.json"));
        Assert.AreEqual(0, api.Maxio.Count(Post, "/subscriptions.json"));
    }

    [TestMethod]
    public async Task ConnectionDropAfterProviderActedIsSettledByLookup()
    {
        using var api = new MaxioTestApi();
        var (client, _) = await api.NewShopperAsync();
        api.Maxio.DropConnectionAfterCreatingSubscription = true;

        var response = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "test-pro" });

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        Assert.AreEqual("test-pro", (await Json(response)).GetProperty("subscription").GetProperty("planHandle").GetString());
        Assert.AreEqual(1, api.Maxio.Count(Post, "/subscriptions.json"), "the write must not be resent");
        Assert.AreEqual(1, api.Maxio.Count(Get, "/subscriptions/lookup.json"));
    }

    [TestMethod]
    public async Task CustomerCreateConnectionDropIsSettledByReferenceLookup()
    {
        using var api = new MaxioTestApi();
        var (client, _) = await api.NewShopperAsync();
        api.Maxio.DropConnectionAfterCreatingCustomer = true;

        var response = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "test-pro" });

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        Assert.AreEqual(1, api.Maxio.Count(Post, "/customers.json"), "the customer create must not be resent");
        Assert.AreEqual(2, api.Maxio.Count(Get, "/customers/lookup.json")); // pre-create lookup + settling re-read
        var subscriptionBody = JsonDocument.Parse(api.Maxio.Bodies(Post, "/subscriptions.json").Single()).RootElement;
        Assert.AreEqual(1001, subscriptionBody.GetProperty("subscription").GetProperty("customer_id").GetInt32());
    }

    [TestMethod]
    public async Task ConnectionDropBeforeProviderActedReleasesTheClaimForARetry()
    {
        using var api = new MaxioTestApi();
        var (client, _) = await api.NewShopperAsync();
        api.Maxio.DropConnectionBeforeCreatingSubscription = true;

        var failed = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "test-pro" });
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.AreEqual("ProviderUnavailable", (await Json(failed)).GetProperty("Code").GetString());

        var retry = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "test-pro" });
        Assert.AreEqual(HttpStatusCode.Created, retry.StatusCode);
        Assert.AreEqual(2, api.Maxio.Count(Post, "/subscriptions.json"));
    }

    [TestMethod]
    public async Task UnsettledOutcomeIsLeftUnknownAndSettledByTheNextRequest()
    {
        using var api = new MaxioTestApi();
        var (client, _) = await api.NewShopperAsync();
        api.Maxio.DropConnectionAfterCreatingSubscription = true;
        api.Maxio.FailSubscriptionLookups = 3; // the settling re-read (and its SDK retries) fails too

        var unknown = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "test-pro" });
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, unknown.StatusCode);
        var unknownJson = await Json(unknown);
        Assert.AreEqual("OutcomeUnknown", unknownJson.GetProperty("Code").GetString());
        StringAssert.StartsWith(unknownJson.GetProperty("Reference").GetString(), "eshop-sub-");

        var next = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "test-pro" });
        Assert.AreEqual(HttpStatusCode.OK, next.StatusCode);
        Assert.AreEqual(unknownJson.GetProperty("Reference").GetString(),
            (await Json(next)).GetProperty("subscription").GetProperty("reference").GetString());
        Assert.AreEqual(1, api.Maxio.Count(Post, "/subscriptions.json"), "settling must never create a second subscription");
    }

    [TestMethod]
    public async Task ProviderRejectionIsReturnedAsUnprocessableAndReleasesTheClaim()
    {
        using var api = new MaxioTestApi();
        var (client, _) = await api.NewShopperAsync();
        api.Maxio.RejectNextSubscriptionWith = "No payment method was on file";

        var rejected = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "test-pro" });
        Assert.AreEqual((HttpStatusCode)422, rejected.StatusCode);
        var json = await Json(rejected);
        Assert.AreEqual("No payment method was on file", json.GetProperty("Details")[0].GetString());

        var retry = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "test-pro" });
        Assert.AreEqual(HttpStatusCode.Created, retry.StatusCode);
    }

    [TestMethod]
    public async Task MySubscriptionsListsOnlyTheCallersSubscriptions()
    {
        using var api = new MaxioTestApi();
        var (alice, _) = await api.NewShopperAsync();
        var (bob, _) = await api.NewShopperAsync();

        Assert.AreEqual(0, (await Json(await alice.GetAsync("api/my-subscriptions"))).GetProperty("subscriptions").GetArrayLength());
        await alice.PostAsJsonAsync("api/subscriptions", new { planHandle = "test-pro" });
        await alice.PostAsJsonAsync("api/subscriptions", new { planHandle = "test-basic" });
        await bob.PostAsJsonAsync("api/subscriptions", new { planHandle = "test-basic" });

        var aliceSubs = (await Json(await alice.GetAsync("api/my-subscriptions"))).GetProperty("subscriptions");
        var bobSubs = (await Json(await bob.GetAsync("api/my-subscriptions"))).GetProperty("subscriptions");

        Assert.AreEqual(2, aliceSubs.GetArrayLength());
        Assert.AreEqual(1, bobSubs.GetArrayLength());
        Assert.AreEqual("test-basic", bobSubs[0].GetProperty("planHandle").GetString());
    }

    [TestMethod]
    public void HostRefusesToStartWithoutMaxioApiKey()
    {
        using var api = new MaxioTestApi(new() { ["Maxio:ApiKey"] = "" });

        var ex = Assert.ThrowsException<OptionsValidationException>(() => api.CreateClient());
        StringAssert.Contains(ex.Message, "Maxio:ApiKey is not configured");
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}
