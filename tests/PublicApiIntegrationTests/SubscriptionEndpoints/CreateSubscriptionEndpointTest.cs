using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class CreateSubscriptionEndpointTest
{
    private const string CustomersPath = "/customers.json";
    private const string SubscriptionsPath = "/subscriptions.json";

    private static Task<HttpResponseMessage> Subscribe(HttpClient client, string planHandle) =>
        client.PostAsJsonAsync("api/subscriptions", new { planHandle });

    private static async Task<JsonNode> ReadJson(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

    [TestMethod]
    public async Task CreatesCustomerAndSubscriptionAndConfirmsPlanPriceStateAndNextBilling()
    {
        var maxio = new FakeMaxio();
        using var app = new MaxioTestApplication(maxio);

        var response = await Subscribe(app.ClientFor(ApiTokenHelper.GetNormalUserToken()), "pro");

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
        var body = await ReadJson(response);
        Assert.AreEqual("created", (string)body["outcome"]!);
        var subscription = body["subscription"]!;
        Assert.AreEqual("pro", (string)subscription["planHandle"]!);
        Assert.AreEqual(299m, (decimal)subscription["price"]!);
        Assert.AreEqual("active", (string)subscription["state"]!);
        Assert.AreEqual(new DateTimeOffset(2026, 11, 14, 0, 0, 0, TimeSpan.Zero), DateTimeOffset.Parse((string)subscription["nextBillingAt"]!));

        Assert.AreEqual(1, maxio.Count(HttpMethod.Post, CustomersPath));
        Assert.AreEqual(1, maxio.Count(HttpMethod.Post, SubscriptionsPath));

        // The buyer comes from the token; the subscription carries the deterministic reference and is invoiced.
        var customerBody = JsonNode.Parse(maxio.Requests.Single(r => r.Path == CustomersPath).Body!)!["customer"]!;
        Assert.AreEqual("eshop:demouser@microsoft.com", (string)customerBody["reference"]!);
        Assert.AreEqual("demouser@microsoft.com", (string)customerBody["email"]!);
        var subscriptionBody = JsonNode.Parse(maxio.Requests.Single(r => r.Path == SubscriptionsPath).Body!)!["subscription"]!;
        Assert.AreEqual("pro", (string)subscriptionBody["product_handle"]!);
        Assert.AreEqual("eshop:demouser@microsoft.com:pro", (string)subscriptionBody["reference"]!);
        Assert.AreEqual("remittance", (string)subscriptionBody["payment_collection_method"]!);
    }

    [TestMethod]
    public async Task DoubleSubmitCreatesExactlyOneCustomerAndOneSubscription()
    {
        var maxio = new FakeMaxio { SubscriptionCreateDelay = TimeSpan.FromMilliseconds(500) };
        using var app = new MaxioTestApplication(maxio);
        var client = app.ClientFor(ApiTokenHelper.GetNormalUserToken());

        var responses = await Task.WhenAll(Subscribe(client, "pro"), Subscribe(client, "pro"), Subscribe(client, "pro"));

        Assert.AreEqual(1, maxio.Count(HttpMethod.Post, CustomersPath));
        Assert.AreEqual(1, maxio.Count(HttpMethod.Post, SubscriptionsPath));
        Assert.AreEqual(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.IsTrue(responses.All(r => r.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK or HttpStatusCode.Conflict),
            string.Join(" | ", await Task.WhenAll(responses.Select(async r => $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}"))));
    }

    [TestMethod]
    public async Task RepeatingTheRequestReturnsTheExistingSubscription()
    {
        var maxio = new FakeMaxio();
        using var app = new MaxioTestApplication(maxio);
        var client = app.ClientFor(ApiTokenHelper.GetNormalUserToken());

        var first = await Subscribe(client, "basic");
        var second = await Subscribe(client, "basic");

        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
        var body = await ReadJson(second);
        Assert.AreEqual("already_subscribed", (string)body["outcome"]!);
        Assert.AreEqual((int)(await ReadJson(first))["subscription"]!["subscriptionId"]!, (int)body["subscription"]!["subscriptionId"]!);
        Assert.AreEqual(1, maxio.Count(HttpMethod.Post, SubscriptionsPath));
    }

    [TestMethod]
    public async Task ExistingMaxioCustomerIsReusedNotRecreated()
    {
        var maxio = new FakeMaxio();
        maxio.SeedCustomer("eshop:demouser@microsoft.com", 777);
        using var app = new MaxioTestApplication(maxio);

        var response = await Subscribe(app.ClientFor(ApiTokenHelper.GetNormalUserToken()), "pro");

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        Assert.AreEqual(0, maxio.Count(HttpMethod.Post, CustomersPath));
        var subscriptionBody = JsonNode.Parse(maxio.Requests.Single(r => r.Path == SubscriptionsPath).Body!)!["subscription"]!;
        Assert.AreEqual(777, (int)subscriptionBody["customer_id"]!);
    }

    [TestMethod]
    public async Task PlanNotOfferedIsRejectedWithoutAnyProviderWrite()
    {
        var maxio = new FakeMaxio();
        using var app = new MaxioTestApplication(maxio);

        var response = await Subscribe(app.ClientFor(ApiTokenHelper.GetNormalUserToken()), "api-call");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual(0, maxio.Requests.Count(r => r.Method == HttpMethod.Post));
    }

    [TestMethod]
    public async Task ProviderRefusalIsReturnedAndReleasesTheClaim()
    {
        var maxio = new FakeMaxio { SubscriptionCreateFailure = FakeMaxio.WriteFailure.Reject };
        using var app = new MaxioTestApplication(maxio);
        var client = app.ClientFor(ApiTokenHelper.GetNormalUserToken());

        var refused = await Subscribe(client, "pro");

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        var body = await ReadJson(refused);
        Assert.AreEqual("Product is not available", (string)body["Errors"]![0]!);

        // The claim was released, so a retry reaches Maxio again rather than being reported "in progress".
        maxio.SubscriptionCreateFailure = FakeMaxio.WriteFailure.None;
        var retried = await Subscribe(client, "pro");
        Assert.AreEqual(HttpStatusCode.Created, retried.StatusCode);
        Assert.AreEqual(2, maxio.Count(HttpMethod.Post, SubscriptionsPath));
    }

    [TestMethod]
    public async Task SubscriptionCreateWhoseConnectionDropsAfterMaxioActedIsSettledAsCreated()
    {
        var maxio = new FakeMaxio { SubscriptionCreateFailure = FakeMaxio.WriteFailure.DropAfterActing };
        using var app = new MaxioTestApplication(maxio);

        var response = await Subscribe(app.ClientFor(ApiTokenHelper.GetNormalUserToken()), "pro");

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJson(response);
        Assert.AreEqual("created", (string)body["outcome"]!);
        Assert.AreEqual("active", (string)body["subscription"]!["state"]!);
        // The POST is never resent; the outcome is settled by looking the reference up.
        Assert.AreEqual(1, maxio.Count(HttpMethod.Post, SubscriptionsPath));
        Assert.IsTrue(maxio.Count(HttpMethod.Get, "/subscriptions/lookup.json") >= 2);
    }

    [TestMethod]
    public async Task SubscriptionCreateWhoseConnectionDropsBeforeMaxioActedIsReportedPendingNotFailed()
    {
        var maxio = new FakeMaxio { SubscriptionCreateFailure = FakeMaxio.WriteFailure.DropBeforeActing };
        using var app = new MaxioTestApplication(maxio);
        var client = app.ClientFor(ApiTokenHelper.GetNormalUserToken());

        var response = await Subscribe(client, "pro");

        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);
        Assert.AreEqual("pending", (string)(await ReadJson(response))["outcome"]!);

        var mine = await ReadJson(await client.GetAsync("api/my-subscriptions"));
        Assert.AreEqual(0, mine["subscriptions"]!.AsArray().Count);
        Assert.AreEqual("eshop:demouser@microsoft.com:pro", (string)mine["pending"]![0]!["reference"]!);
        Assert.AreEqual("Unknown", (string)mine["pending"]![0]!["status"]!);

        // While the unknown outcome is fresh, a retry is held back instead of risking a second subscription.
        maxio.SubscriptionCreateFailure = FakeMaxio.WriteFailure.None;
        var retry = await Subscribe(client, "pro");
        Assert.AreEqual(HttpStatusCode.Conflict, retry.StatusCode);
        Assert.AreEqual(1, maxio.Count(HttpMethod.Post, SubscriptionsPath));
    }

    [TestMethod]
    public async Task CustomerCreateWhoseConnectionDropsAfterMaxioActedIsSettledAndSubscribeContinues()
    {
        var maxio = new FakeMaxio { CustomerCreateFailure = FakeMaxio.WriteFailure.DropAfterActing };
        using var app = new MaxioTestApplication(maxio);

        var response = await Subscribe(app.ClientFor(ApiTokenHelper.GetNormalUserToken()), "pro");

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        Assert.AreEqual(1, maxio.Count(HttpMethod.Post, CustomersPath));
        Assert.AreEqual(1, maxio.Count(HttpMethod.Post, SubscriptionsPath));
    }

    [TestMethod]
    public async Task CustomerCreateWhoseConnectionDropsBeforeMaxioActedIsReportedPending()
    {
        var maxio = new FakeMaxio { CustomerCreateFailure = FakeMaxio.WriteFailure.DropBeforeActing };
        using var app = new MaxioTestApplication(maxio);

        var response = await Subscribe(app.ClientFor(ApiTokenHelper.GetNormalUserToken()), "pro");

        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);
        Assert.AreEqual(1, maxio.Count(HttpMethod.Post, CustomersPath));
        Assert.AreEqual(0, maxio.Count(HttpMethod.Post, SubscriptionsPath));
    }
}
