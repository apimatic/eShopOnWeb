using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// End-to-end subscribe flow against the configured Maxio sandbox. Skipped (inconclusive) on a
/// machine with no MAXIO_* environment variables, so the suite stays runnable without credentials.
/// </summary>
[TestClass]
public class SubscribeFlowTest
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static SubscriptionApiFactory _factory = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext _) => _factory = new SubscriptionApiFactory();

    [ClassCleanup]
    public static void ClassCleanup() => _factory.Dispose();

    [TestMethod]
    public async Task PlansAreListedFromTheBillingCatalog()
    {
        var client = AuthenticatedClient();

        var response = await client.GetAsync("api/subscription-plans");
        RequireConfigured(response);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var plans = await ReadPlansAsync(response);
        Assert.IsTrue(plans.Count > 0, "the sandbox product family should publish at least one plan");
        Assert.IsTrue(plans.All(plan => !string.IsNullOrWhiteSpace(plan.Handle)), "every plan must expose a handle");
        Assert.IsTrue(plans.All(plan => plan.PriceInCents >= 0));

        // Numeric plan ids are reassigned when the sandbox catalog is re-seeded, so they must never
        // reach callers as the way to identify a plan.
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        foreach (var publishedPlan in document.RootElement.GetProperty("subscriptionPlans").EnumerateArray())
        {
            Assert.IsFalse(
                publishedPlan.TryGetProperty("id", out _),
                "plans are addressed by handle; numeric ids must not be published");
        }
    }

    [TestMethod]
    public async Task SubscribingIsIdempotentAndShowsUpOnTheShoppersAccount()
    {
        var client = AuthenticatedClient();

        var plansResponse = await client.GetAsync("api/subscription-plans");
        RequireConfigured(plansResponse);
        var plan = (await ReadPlansAsync(plansResponse)).First();

        var first = await SubscribeAsync(client, plan.Handle);
        Assert.IsTrue(
            first.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK,
            $"expected 201 or 200 but got {(int)first.StatusCode}: {await first.Content.ReadAsStringAsync()}");

        var firstResult = await ReadSubscribeResultAsync(first);
        Assert.IsNotNull(firstResult.Subscription);
        Assert.AreEqual(plan.Handle, firstResult.Subscription!.PlanHandle);
        Assert.IsTrue(firstResult.Subscription.IsLive, $"unexpected state: {firstResult.Subscription.State}");
        Assert.IsNotNull(firstResult.Subscription.NextBillingDate, "the shopper must be told when they will next be billed");

        // A repeat of the same request — the double-click — must resolve to the same subscription.
        var second = await SubscribeAsync(client, plan.Handle);
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);

        var secondResult = await ReadSubscribeResultAsync(second);
        Assert.IsFalse(secondResult.Created, "a repeated subscribe must not enroll the shopper again");
        Assert.AreEqual(firstResult.Subscription.Id, secondResult.Subscription!.Id);
        Assert.AreEqual(firstResult.Subscription.CustomerId, secondResult.Subscription.CustomerId);

        var mine = await client.GetFromJsonAsync<ListMySubscriptionsPayload>("api/my-subscriptions", Json);
        Assert.IsNotNull(mine);
        Assert.AreEqual(
            1,
            mine!.Subscriptions.Count(subscription => subscription.Id == firstResult.Subscription.Id),
            "the subscription should appear exactly once on the shopper's account");
    }

    [TestMethod]
    public async Task ConcurrentSubscribesProduceASingleSubscription()
    {
        var client = AuthenticatedClient();

        var plansResponse = await client.GetAsync("api/subscription-plans");
        RequireConfigured(plansResponse);
        var plan = (await ReadPlansAsync(plansResponse)).First();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => SubscribeAsync(client, plan.Handle)));
        var results = await Task.WhenAll(responses.Select(ReadSubscribeResultAsync));

        var distinctIds = results.Select(result => result.Subscription!.Id).Distinct().ToList();
        Assert.AreEqual(1, distinctIds.Count, "concurrent subscribes must converge on one subscription");
        Assert.IsTrue(results.Count(result => result.Created) <= 1, "at most one caller may report a new enrollment");
    }

    [TestMethod]
    public async Task SubscribingToAnUnpublishedPlanIsNotFound()
    {
        var client = AuthenticatedClient();

        var response = await SubscribeAsync(client, "definitely-not-a-real-plan-handle");
        RequireConfigured(response);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribingWithoutAPlanHandleIsRejected()
    {
        var client = AuthenticatedClient();

        var response = await client.PostAsync("api/subscriptions",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        RequireConfigured(response);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static HttpClient AuthenticatedClient()
    {
        if (!SubscriptionApiFactory.MaxioIsConfigured)
        {
            Assert.Inconclusive("Set MAXIO_API_KEY, MAXIO_SITE_SUBDOMAIN and MAXIO_DEFAULT_PRODUCT_FAMILY to run the live subscribe flow.");
        }

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        return client;
    }

    private static Task<HttpResponseMessage> SubscribeAsync(HttpClient client, string planHandle) =>
        client.PostAsJsonAsync("api/subscriptions", new { planHandle }, Json);

    private static void RequireConfigured(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            Assert.Inconclusive("The test host did not pick up Maxio configuration; the subscription capability reported 503.");
        }
    }

    private static async Task<List<PlanPayload>> ReadPlansAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ListPlansPayload>(Json))!.SubscriptionPlans;

    private static async Task<SubscribePayload> ReadSubscribeResultAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<SubscribePayload>(Json))!;

    private sealed class ListPlansPayload
    {
        public List<PlanPayload> SubscriptionPlans { get; set; } = new();
    }

    private sealed class PlanPayload
    {
        public string Handle { get; set; } = string.Empty;
        public long PriceInCents { get; set; }
    }

    private sealed class SubscribePayload
    {
        public SubscriptionPayload? Subscription { get; set; }
        public bool Created { get; set; }
    }

    private sealed class ListMySubscriptionsPayload
    {
        public List<SubscriptionPayload> Subscriptions { get; set; } = new();
    }

    private sealed class SubscriptionPayload
    {
        public long Id { get; set; }
        public string State { get; set; } = string.Empty;
        public bool IsLive { get; set; }
        public string? PlanHandle { get; set; }
        public DateTimeOffset? NextBillingDate { get; set; }
        public long CustomerId { get; set; }
    }
}
