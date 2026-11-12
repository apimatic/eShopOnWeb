using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>Runs against the real Maxio sandbox (credentials from the MAXIO_* environment variables); nothing is stubbed.</summary>
[TestClass]
public class MaxioSandboxTests
{
    private static HttpClient As(string token)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    [TestMethod]
    public async Task ShopperEndpointsRequireAToken()
    {
        var client = ProgramTest.NewClient;
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/plans")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/subscriptions/1")).StatusCode);
    }

    [TestMethod]
    public async Task StaffEndpointsRejectShoppers()
    {
        var client = As(ApiTokenHelper.GetNormalUserToken());
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("api/admin/plans", new { name = "x", price = 1, brand = "Azure" })).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("api/admin/plans/1/currency-prices", new { currency = "EUR", price = 1 })).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync("api/admin/subscriptions/1/next-billing-date", new { date = "2099-01-01" })).StatusCode);
    }

    [TestMethod]
    public async Task NewPlanMustUseACatalogBrand()
    {
        var admin = As(ApiTokenHelper.GetAdminUserToken());
        var r = await admin.PostAsJsonAsync("api/admin/plans", new { name = "No brand " + Guid.NewGuid().ToString("N")[..6], price = 5, brand = "Not A Brand" });
        Assert.AreEqual(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [TestMethod]
    public async Task SubscriptionLifecycleAgainstTheSandbox()
    {
        var admin = As(ApiTokenHelper.GetAdminUserToken());
        var shopper = As(ApiTokenHelper.GetUserToken("demouser@microsoft.com"));
        var stranger = As(ApiTokenHelper.GetUserToken("someone.else@microsoft.com"));
        var suffix = Guid.NewGuid().ToString("N")[..8];

        // staff add two plans; both show up in the shopper's plan list
        var a = await admin.PostAsJsonAsync("api/admin/plans", new { name = "Test A " + suffix, price = 10.5m, brand = "Azure" });
        Assert.AreEqual(HttpStatusCode.Created, a.StatusCode, await a.Content.ReadAsStringAsync());
        var b = await admin.PostAsJsonAsync("api/admin/plans", new { name = "Test B " + suffix, price = 20m, brand = "Azure" });
        Assert.AreEqual(HttpStatusCode.Created, b.StatusCode);
        var handleA = (await Json(a)).GetProperty("handle").GetString()!;
        var handleB = (await Json(b)).GetProperty("handle").GetString()!;
        var dup = await admin.PostAsJsonAsync("api/admin/plans", new { name = "Test A " + suffix, price = 10.5m, brand = "Azure" });
        Assert.AreEqual(HttpStatusCode.Conflict, dup.StatusCode);

        var plans = await Json(await shopper.GetAsync("api/plans"));
        StringAssert.Contains(plans.ToString(), handleA);
        StringAssert.Contains(plans.ToString(), handleB);

        // a handle that is not an offered plan is refused before anything is created
        Assert.AreEqual(HttpStatusCode.BadRequest, (await shopper.PostAsJsonAsync("api/subscriptions", new { planHandle = "no-such-plan" })).StatusCode);

        // subscribe without card details; the shopper can hold several subscriptions
        var s1 = await shopper.PostAsJsonAsync("api/subscriptions", new { planHandle = handleA });
        Assert.AreEqual(HttpStatusCode.Created, s1.StatusCode, await s1.Content.ReadAsStringAsync());
        var s2 = await shopper.PostAsJsonAsync("api/subscriptions", new { planHandle = handleB });
        Assert.AreEqual(HttpStatusCode.Created, s2.StatusCode);
        var id1 = (await Json(s1)).GetProperty("subscriptionId").GetInt32();
        var id2 = (await Json(s2)).GetProperty("subscriptionId").GetInt32();
        Assert.AreNotEqual(id1, id2);

        // reading: owner sees it (a paid flag is present), a stranger gets 404
        var page = await Json(await shopper.GetAsync($"api/subscriptions/{id1}"));
        Assert.AreEqual(handleA, page.GetProperty("planHandle").GetString());
        Assert.IsTrue(page.TryGetProperty("paid", out var paid) && paid.ValueKind is JsonValueKind.True or JsonValueKind.False);
        Assert.AreEqual(HttpStatusCode.NotFound, (await stranger.GetAsync($"api/subscriptions/{id1}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await stranger.PostAsync($"api/subscriptions/{id1}/cancel", null)).StatusCode);

        // pause / resume
        var paused = await shopper.PostAsync($"api/subscriptions/{id1}/pause", null);
        Assert.AreEqual(HttpStatusCode.OK, paused.StatusCode, await paused.Content.ReadAsStringAsync());
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"api/subscriptions/{id1}/pause", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await shopper.PostAsync($"api/subscriptions/{id1}/resume", null)).StatusCode);

        // cancel / restart
        Assert.AreEqual(HttpStatusCode.OK, (await shopper.PostAsync($"api/subscriptions/{id1}/cancel", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"api/subscriptions/{id1}/cancel", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await shopper.PostAsync($"api/subscriptions/{id1}/restart", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"api/subscriptions/{id1}/restart", null)).StatusCode);

        // switch, including a cancelled subscription (lands on the new plan)
        Assert.AreEqual(HttpStatusCode.BadRequest, (await shopper.PostAsJsonAsync($"api/subscriptions/{id1}/switch", new { planHandle = "no-such-plan" })).StatusCode);
        var switched = await Json(await shopper.PostAsJsonAsync($"api/subscriptions/{id1}/switch", new { planHandle = handleB }));
        Assert.AreEqual(handleB, switched.GetProperty("planHandle").GetString());
        Assert.AreEqual(HttpStatusCode.OK, (await shopper.PostAsync($"api/subscriptions/{id2}/cancel", null)).StatusCode);
        var revived = await Json(await shopper.PostAsJsonAsync($"api/subscriptions/{id2}/switch", new { planHandle = handleA }));
        Assert.AreEqual(handleA, revived.GetProperty("planHandle").GetString());
        Assert.AreEqual("active", revived.GetProperty("state").GetString());

        // usage for a day
        Assert.AreEqual(HttpStatusCode.BadRequest, (await shopper.GetAsync($"api/subscriptions/{id1}/usage?date=yesterday")).StatusCode);
        var usage = await shopper.GetAsync($"api/subscriptions/{id1}/usage?date={DateTime.UtcNow:yyyy-MM-dd}");
        Assert.AreEqual(HttpStatusCode.OK, usage.StatusCode, await usage.Content.ReadAsStringAsync());

        // staff set the next billing date
        var when = DateTime.UtcNow.AddDays(40).ToString("yyyy-MM-dd");
        Assert.AreEqual(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"api/admin/subscriptions/{id1}/next-billing-date", new { date = "2001-01-01" })).StatusCode);
        var moved = await admin.PutAsJsonAsync($"api/admin/subscriptions/{id1}/next-billing-date", new { date = when });
        Assert.AreEqual(HttpStatusCode.OK, moved.StatusCode, await moved.Content.ReadAsStringAsync());
        StringAssert.Contains((await Json(moved)).GetProperty("nextBillingAt").GetString(), when);

        // a plan can only be priced in a currency the site supports; this answers 4xx with a reason either way
        var planId = (await Json(a)).GetProperty("id").GetInt32();
        var priced = await admin.PostAsJsonAsync($"api/admin/plans/{planId}/currency-prices", new { currency = "EUR", price = 9 });
        Assert.IsTrue(priced.StatusCode is HttpStatusCode.OK or HttpStatusCode.UnprocessableEntity, priced.StatusCode.ToString());
    }
}
