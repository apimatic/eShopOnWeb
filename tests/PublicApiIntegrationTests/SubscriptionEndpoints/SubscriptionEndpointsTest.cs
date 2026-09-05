using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Exercises the subscription-billing endpoints against the real Maxio sandbox configured
/// via user-secrets/environment (see Maxio:ApiKey / Maxio:Subdomain / Maxio:ProductFamilyHandle).
/// These are live integration tests, not mocks: they are the self-verification that the
/// spec-driven Maxio client actually round-trips against the sandbox. They no-op (rather
/// than fail) when Maxio isn't configured, so the suite stays green for anyone who clones
/// the repo without sandbox credentials.
/// </summary>
[TestClass]
public class SubscriptionEndpointsTest
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public async Task ReturnsUnauthorizedWithoutToken()
    {
        var client = ProgramTest.NewClient;

        var plansResponse = await client.GetAsync("api/subscription-plans");
        var subscribeResponse = await client.PostAsync("api/subscriptions", JsonBody(new { planHandle = "anything" }));
        var mineResponse = await client.GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, plansResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, subscribeResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, mineResponse.StatusCode);
    }

    [TestMethod]
    public async Task ListsSeededPlansFromMaxioSandbox()
    {
        if (!MaxioSandboxConfigured())
        {
            Assert.Inconclusive("MAXIO_API_KEY is not set in this environment; skipping live Maxio sandbox test.");
            return;
        }

        var client = AuthenticatedClient(ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync("api/subscription-plans");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ListSubscriptionPlansResponse>(body, JsonOptions)!;

        Assert.IsTrue(result.Plans.Count > 0, "Expected at least one seeded plan in the configured product family.");
        foreach (var plan in result.Plans)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(plan.Handle));
            Assert.IsFalse(string.IsNullOrWhiteSpace(plan.Name));
            Assert.IsTrue(plan.PriceInCents > 0);
        }
    }

    [TestMethod]
    public async Task SubscribingTwiceIsIdempotentAndVisibleInMySubscriptions()
    {
        if (!MaxioSandboxConfigured())
        {
            Assert.Inconclusive("MAXIO_API_KEY is not set in this environment; skipping live Maxio sandbox test.");
            return;
        }

        // A distinct token per test run keeps this independent of any subscriptions left
        // over from prior runs against the same shared sandbox site/customer reference.
        var client = AuthenticatedClient(ApiTokenHelper.GetNormalUserToken());

        var plansResponse = await client.GetAsync("api/subscription-plans");
        plansResponse.EnsureSuccessStatusCode();
        var plans = JsonSerializer.Deserialize<ListSubscriptionPlansResponse>(await plansResponse.Content.ReadAsStringAsync(), JsonOptions)!;
        Assert.IsTrue(plans.Plans.Count > 0, "Need at least one seeded plan to subscribe to.");
        var planHandle = plans.Plans[0].Handle;

        var firstResponse = await client.PostAsync("api/subscriptions", JsonBody(new { planHandle }));
        Assert.AreEqual(HttpStatusCode.Created, firstResponse.StatusCode);
        var first = JsonSerializer.Deserialize<CreateSubscriptionResponse>(await firstResponse.Content.ReadAsStringAsync(), JsonOptions)!;
        Assert.IsFalse(first.AlreadyExisted);

        var secondResponse = await client.PostAsync("api/subscriptions", JsonBody(new { planHandle }));
        Assert.AreEqual(HttpStatusCode.OK, secondResponse.StatusCode);
        var second = JsonSerializer.Deserialize<CreateSubscriptionResponse>(await secondResponse.Content.ReadAsStringAsync(), JsonOptions)!;
        Assert.IsTrue(second.AlreadyExisted);

        Assert.AreEqual(first.Subscription.MaxioSubscriptionId, second.Subscription.MaxioSubscriptionId,
            "A double-click must not create a second subscription.");

        var mineResponse = await client.GetAsync("api/my-subscriptions");
        mineResponse.EnsureSuccessStatusCode();
        var mine = JsonSerializer.Deserialize<MySubscriptionsResponse>(await mineResponse.Content.ReadAsStringAsync(), JsonOptions)!;
        Assert.IsTrue(mine.Subscriptions.Any(s => s.MaxioSubscriptionId == first.Subscription.MaxioSubscriptionId));
    }

    [TestMethod]
    public async Task SubscribingToUnknownPlanHandleReturnsClientError()
    {
        if (!MaxioSandboxConfigured())
        {
            Assert.Inconclusive("MAXIO_API_KEY is not set in this environment; skipping live Maxio sandbox test.");
            return;
        }

        var client = AuthenticatedClient(ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsync("api/subscriptions", JsonBody(new { planHandle = "this-plan-handle-does-not-exist" }));

        Assert.IsTrue((int)response.StatusCode is >= 400 and < 500, $"Expected a 4xx passthrough, got {(int)response.StatusCode}.");
    }

    private static bool MaxioSandboxConfigured()
        => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MAXIO_API_KEY"));

    private static HttpClient AuthenticatedClient(string token)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static StringContent JsonBody(object value)
        => new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");
}
