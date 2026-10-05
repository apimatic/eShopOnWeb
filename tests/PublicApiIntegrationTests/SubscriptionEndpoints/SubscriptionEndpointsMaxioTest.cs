using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Live tests against the Maxio Advanced Billing sandbox. They run only when the
/// sandbox credentials are present in the environment (MAXIO_API_KEY etc.); otherwise
/// they report inconclusive. No credential values are stored in this repository.
/// </summary>
[TestClass]
public class SubscriptionEndpointsMaxioTest
{
    private static HttpClient AuthedClient()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        return client;
    }

    private static bool SandboxConfigured()
    {
        return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MAXIO_API_KEY"));
    }

    [TestMethod]
    public async Task SubscriptionPlansReturnsSandboxPlans()
    {
        if (!SandboxConfigured())
        {
            Assert.Inconclusive("Maxio sandbox credentials are not configured in this environment.");
        }

        var client = AuthedClient();
        var response = await client.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ListSubscriptionPlansResponse>();
        Assert.IsNotNull(body);
        Assert.IsTrue(body!.Plans.Count > 0, "The configured Maxio product family should hold at least one plan.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(body.Plans[0].Handle));
    }

    [TestMethod]
    public async Task SubscribeAndRepeatReturnsSameSubscription()
    {
        if (!SandboxConfigured())
        {
            Assert.Inconclusive("Maxio sandbox credentials are not configured in this environment.");
        }

        var client = AuthedClient();

        var plansResponse = await client.GetFromJsonAsync<ListSubscriptionPlansResponse>("api/subscription-plans");
        Assert.IsNotNull(plansResponse);
        var handle = plansResponse!.Plans.FirstOrDefault()?.Handle;
        Assert.IsFalse(string.IsNullOrWhiteSpace(handle), "At least one plan is required to exercise the subscribe flow.");

        var first = await client.PostAsJsonAsync("api/subscriptions", new { productHandle = handle });
        Assert.IsTrue(
            first.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created,
            $"Unexpected status {(int)first.StatusCode}: {await first.Content.ReadAsStringAsync()}");
        var firstBody = await first.Content.ReadFromJsonAsync<CreateSubscriptionResponse>();
        Assert.IsNotNull(firstBody?.Subscription);
        var subscriptionId = firstBody!.Subscription!.MaxioSubscriptionId;
        Assert.AreNotEqual(0, subscriptionId);
        Assert.AreEqual(handle, firstBody.Subscription.PlanHandle, ignoreCase: true);
        Assert.IsFalse(string.IsNullOrWhiteSpace(firstBody.Subscription.State));
        Assert.IsFalse(string.IsNullOrWhiteSpace(firstBody.Subscription.Reference));

        // A double-click / repeated request must return the same subscription, not create a second one.
        var second = await client.PostAsJsonAsync("api/subscriptions", new { productHandle = handle });
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
        var secondBody = await second.Content.ReadFromJsonAsync<CreateSubscriptionResponse>();
        Assert.IsNotNull(secondBody?.Subscription);
        Assert.AreEqual(subscriptionId, secondBody!.Subscription!.MaxioSubscriptionId);
        Assert.IsFalse(secondBody.Subscription.Created);
    }

    [TestMethod]
    public async Task SubscribeToUnknownPlanReturnsNotFound()
    {
        if (!SandboxConfigured())
        {
            Assert.Inconclusive("Maxio sandbox credentials are not configured in this environment.");
        }

        var client = AuthedClient();
        var response = await client.PostAsJsonAsync("api/subscriptions", new { productHandle = "no-such-plan-handle" });
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task MySubscriptionsListsTheSubscribedPlan()
    {
        if (!SandboxConfigured())
        {
            Assert.Inconclusive("Maxio sandbox credentials are not configured in this environment.");
        }

        var client = AuthedClient();
        var plansResponse = await client.GetFromJsonAsync<ListSubscriptionPlansResponse>("api/subscription-plans");
        var handle = plansResponse!.Plans.FirstOrDefault()?.Handle;
        Assert.IsFalse(string.IsNullOrWhiteSpace(handle));

        var subscribed = await client.PostAsJsonAsync("api/subscriptions", new { productHandle = handle });
        Assert.AreEqual(HttpStatusCode.OK, subscribed.StatusCode);

        var mine = await client.GetAsync("api/my-subscriptions");
        Assert.AreEqual(HttpStatusCode.OK, mine.StatusCode);
        var body = await mine.Content.ReadFromJsonAsync<MySubscriptionsListResponse>();
        Assert.IsNotNull(body);
        Assert.IsTrue(body!.Subscriptions.Any(s => string.Equals(s.PlanHandle, handle, StringComparison.OrdinalIgnoreCase)),
            "The just-subscribed plan should appear in the shopper's subscription list.");
        Assert.IsTrue(body.Subscriptions.All(s => !string.IsNullOrWhiteSpace(s.State)));
    }
}