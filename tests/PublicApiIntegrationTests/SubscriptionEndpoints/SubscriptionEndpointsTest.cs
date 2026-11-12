using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// These tests call the real Maxio sandbox: the Maxio settings must be available to the app
/// (user-secrets: Maxio:ApiKey, Maxio:Subdomain, Maxio:Environment). Nothing is stubbed.
/// </summary>
[TestClass]
public class SubscriptionEndpointsTest
{
    private static HttpClient ClientFor(string token)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    [TestMethod]
    public async Task PlansRequireSignIn()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task AdminRoutesRejectShoppers()
    {
        var client = ClientFor(ApiTokenHelper.GetNormalUserToken());
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.PostAsync("api/admin/plans", Json("{}"))).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.PostAsync("api/admin/plans/1/currency-prices", Json("{}"))).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden,
            (await client.PutAsync("api/admin/subscriptions/1/next-billing-date", Json("{}"))).StatusCode);
    }

    [TestMethod]
    public async Task CreatePlanRejectsUnknownBrand()
    {
        var client = ClientFor(ApiTokenHelper.GetAdminUserToken());
        var response = await client.PostAsync("api/admin/plans", Json("{\"name\":\"X\",\"price\":5,\"brand\":\"no-such-brand\"}"));
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task UsageRequiresAValidDate()
    {
        var client = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var response = await client.GetAsync("api/subscriptions/1/usage?date=yesterday");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task ShopperSubscriptionLifecycleAgainstSandbox()
    {
        var admin = ClientFor(ApiTokenHelper.GetAdminUserToken());
        var shopper = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var other = ClientFor(ApiTokenHelper.GetAdminUserToken());
        var suffix = Guid.NewGuid().ToString("N")[..6];

        // staff add two plans under a catalog brand
        var planA = await admin.PostAsync("api/admin/plans", Json($"{{\"name\":\"It A {suffix}\",\"price\":4.5,\"brand\":\".NET\"}}"));
        Assert.AreEqual(HttpStatusCode.Created, planA.StatusCode, await planA.Content.ReadAsStringAsync());
        var handleA = (await ReadAsync(planA)).GetProperty("handle").GetString();
        var planB = await admin.PostAsync("api/admin/plans", Json($"{{\"name\":\"It B {suffix}\",\"price\":8,\"brand\":\".NET\"}}"));
        var handleB = (await ReadAsync(planB)).GetProperty("handle").GetString();

        // the shopper sees them
        var plans = await shopper.GetAsync("api/plans");
        Assert.AreEqual(HttpStatusCode.OK, plans.StatusCode);
        StringAssert.Contains(await plans.Content.ReadAsStringAsync(), handleA);

        // an unknown plan is refused with a reason
        var unknown = await shopper.PostAsync("api/subscriptions", Json("{\"planHandle\":\"definitely-not-a-plan\"}"));
        Assert.IsTrue((int)unknown.StatusCode is >= 400 and < 500);

        // subscribe, and hold several plans at once
        var created = await shopper.PostAsync("api/subscriptions", Json($"{{\"planHandle\":\"{handleA}\"}}"));
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());
        var id = (await ReadAsync(created)).GetProperty("subscriptionId").GetInt32();
        var second = await shopper.PostAsync("api/subscriptions", Json($"{{\"planHandle\":\"{handleB}\"}}"));
        Assert.AreEqual(HttpStatusCode.Created, second.StatusCode);

        // the page shows `paid`; another user cannot see the subscription
        var page = await ReadAsync(await shopper.GetAsync($"api/subscriptions/{id}"));
        Assert.IsTrue(page.TryGetProperty("paid", out _));
        Assert.AreEqual(HttpStatusCode.NotFound, (await other.GetAsync($"api/subscriptions/{id}")).StatusCode);

        // pause / resume
        Assert.AreEqual("on_hold", (await ReadAsync(await shopper.PostAsync($"api/subscriptions/{id}/pause", null))).GetProperty("state").GetString());
        Assert.AreEqual("active", (await ReadAsync(await shopper.PostAsync($"api/subscriptions/{id}/resume", null))).GetProperty("state").GetString());

        // cancel, switch while cancelled, restart
        Assert.AreEqual("canceled", (await ReadAsync(await shopper.PostAsync($"api/subscriptions/{id}/cancel", null))).GetProperty("state").GetString());
        var switched = await ReadAsync(await shopper.PostAsync($"api/subscriptions/{id}/switch", Json($"{{\"planHandle\":\"{handleB}\"}}")));
        Assert.AreEqual(handleB, switched.GetProperty("planHandle").GetString());
        var restarted = await ReadAsync(await shopper.PostAsync($"api/subscriptions/{id}/restart", null));
        Assert.AreEqual("active", restarted.GetProperty("state").GetString());

        // usage for a day
        var usage = await shopper.GetAsync($"api/subscriptions/{id}/usage?date={DateTime.UtcNow:yyyy-MM-dd}");
        Assert.AreEqual(HttpStatusCode.OK, usage.StatusCode);

        // staff set the next billing date
        var date = DateTime.UtcNow.AddDays(20).ToString("yyyy-MM-dd");
        var moved = await admin.PutAsync($"api/admin/subscriptions/{id}/next-billing-date", Json($"{{\"date\":\"{date}\"}}"));
        Assert.AreEqual(HttpStatusCode.OK, moved.StatusCode, await moved.Content.ReadAsStringAsync());
    }
}
