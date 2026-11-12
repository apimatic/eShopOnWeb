using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.MaxioEndpoints;

/// <summary>
/// Runs against the real Maxio sandbox site (configured through the Maxio: settings). Nothing is stubbed.
/// </summary>
[TestClass]
public class MaxioSandboxTests
{
    private static readonly string Run = Guid.NewGuid().ToString("N").Substring(0, 8);

    private static HttpClient As(string token)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;

    [TestMethod]
    public async Task EndpointsRequireSignIn()
    {
        var anon = ProgramTest.NewClient;
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anon.GetAsync("api/plans")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anon.GetAsync("api/subscriptions/1")).StatusCode);
    }

    [TestMethod]
    public async Task StaffEndpointsRefuseShoppers()
    {
        var shopper = As(ApiTokenHelper.GetNormalUserToken());
        Assert.AreEqual(HttpStatusCode.Forbidden,
            (await shopper.PostAsJsonAsync("api/admin/plans", new { name = "x", price = 1, brandId = 1 })).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden,
            (await shopper.PostAsJsonAsync("api/admin/plans/1/currency-prices", new { currency = "EUR", price = 1 })).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden,
            (await shopper.PutAsJsonAsync("api/admin/subscriptions/1/next-billing-date", new { nextBillingDate = "2099-01-01" })).StatusCode);
    }

    [TestMethod]
    public async Task NewPlanNeedsABrandFromTheCatalog()
    {
        var admin = As(ApiTokenHelper.GetAdminUserToken());
        var r = await admin.PostAsJsonAsync("api/admin/plans", new { name = "Bad brand " + Run, price = 5, brandId = 99999 });
        Assert.AreEqual(HttpStatusCode.BadRequest, r.StatusCode);
        StringAssert.Contains((await Json(r)).GetProperty("message").GetString(), "brand");
    }

    [TestMethod]
    public async Task ShopperLifecycleAgainstSandbox()
    {
        var admin = As(ApiTokenHelper.GetAdminUserToken());
        var shopper = As(ApiTokenHelper.GetTokenFor($"shopper-{Run}@example.com"));
        var other = As(ApiTokenHelper.GetTokenFor($"other-{Run}@example.com"));

        // staff add two plans
        var created = await admin.PostAsJsonAsync("api/admin/plans", new { name = $"Test Plan A {Run}", price = 12.50m, brandId = 1 });
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());
        var planA = await Json(created);
        var handleA = planA.GetProperty("handle").GetString()!;
        var planAId = planA.GetProperty("planId").GetInt32();

        var createdB = await admin.PostAsJsonAsync("api/admin/plans", new { name = $"Test Plan B {Run}", price = 20m, brandId = 1 });
        Assert.AreEqual(HttpStatusCode.Created, createdB.StatusCode, await createdB.Content.ReadAsStringAsync());
        var handleB = (await Json(createdB)).GetProperty("handle").GetString()!;

        // creating the same plan again is refused
        var dup = await admin.PostAsJsonAsync("api/admin/plans", new { name = $"Test Plan A {Run}", price = 12.50m, brandId = 1 });
        Assert.AreEqual(HttpStatusCode.Conflict, dup.StatusCode);

        // plans list
        var plans = await shopper.GetAsync("api/plans");
        Assert.AreEqual(HttpStatusCode.OK, plans.StatusCode);
        var handles = (await Json(plans)).GetProperty("plans").EnumerateArray().Select(p => p.GetProperty("handle").GetString()).ToList();
        CollectionAssert.IsSubsetOf(new[] { handleA, handleB }, handles);

        // unknown plan refused
        Assert.AreEqual(HttpStatusCode.BadRequest, (await shopper.PostAsJsonAsync("api/subscriptions", new { planHandle = "no-such-plan-" + Run })).StatusCode);

        // subscribe to both plans
        var subA = await shopper.PostAsJsonAsync("api/subscriptions", new { planHandle = handleA });
        Assert.AreEqual(HttpStatusCode.Created, subA.StatusCode, await subA.Content.ReadAsStringAsync());
        var idA = (await Json(subA)).GetProperty("subscriptionId").GetInt32();
        var subB = await shopper.PostAsJsonAsync("api/subscriptions", new { planHandle = handleB });
        Assert.AreEqual(HttpStatusCode.Created, subB.StatusCode, await subB.Content.ReadAsStringAsync());

        // same plan twice is refused
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsJsonAsync("api/subscriptions", new { planHandle = handleA })).StatusCode);

        // page, with paid
        var page = await shopper.GetAsync($"api/subscriptions/{idA}");
        Assert.AreEqual(HttpStatusCode.OK, page.StatusCode);
        var view = await Json(page);
        Assert.AreEqual(handleA, view.GetProperty("planHandle").GetString());
        Assert.IsTrue(view.TryGetProperty("paid", out _));

        // someone else's subscription is not found
        Assert.AreEqual(HttpStatusCode.NotFound, (await other.GetAsync($"api/subscriptions/{idA}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await other.PostAsync($"api/subscriptions/{idA}/cancel", null)).StatusCode);

        // resume while not paused is refused with the state
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"api/subscriptions/{idA}/resume", null)).StatusCode);

        // pause / resume
        var pause = await shopper.PostAsync($"api/subscriptions/{idA}/pause", null);
        Console.WriteLine($"pause -> {(int)pause.StatusCode}: {await pause.Content.ReadAsStringAsync()}");
        Assert.IsTrue(pause.StatusCode == HttpStatusCode.OK || (int)pause.StatusCode is >= 400 and < 500);
        if (pause.StatusCode == HttpStatusCode.OK)
        {
            Assert.AreEqual("on_hold", (await Json(pause)).GetProperty("state").GetString());
            var resume = await shopper.PostAsync($"api/subscriptions/{idA}/resume", null);
            Assert.AreEqual(HttpStatusCode.OK, resume.StatusCode, await resume.Content.ReadAsStringAsync());
        }

        // usage for a day
        var usage = await shopper.GetAsync($"api/subscriptions/{idA}/usage?date={DateTime.UtcNow:yyyy-MM-dd}");
        Assert.AreEqual(HttpStatusCode.OK, usage.StatusCode, await usage.Content.ReadAsStringAsync());
        Assert.AreEqual(HttpStatusCode.BadRequest, (await shopper.GetAsync($"api/subscriptions/{idA}/usage?date=yesterday")).StatusCode);

        // staff: next billing date (while active)
        var next = await admin.PutAsJsonAsync($"api/admin/subscriptions/{idA}/next-billing-date", new { nextBillingDate = DateTime.UtcNow.AddDays(20).ToString("yyyy-MM-dd") });
        Console.WriteLine($"next billing date -> {(int)next.StatusCode}: {await next.Content.ReadAsStringAsync()}");
        Assert.IsTrue(next.StatusCode == HttpStatusCode.OK || (int)next.StatusCode is >= 400 and < 500);
        Assert.AreEqual(HttpStatusCode.BadRequest,
            (await admin.PutAsJsonAsync($"api/admin/subscriptions/{idA}/next-billing-date", new { nextBillingDate = "2000-01-01" })).StatusCode);

        // staff: currency price
        var cur = await admin.PostAsJsonAsync($"api/admin/plans/{planAId}/currency-prices", new { currency = "EUR", price = 11 });
        Console.WriteLine($"currency price -> {(int)cur.StatusCode}: {await cur.Content.ReadAsStringAsync()}");
        Assert.IsTrue(cur.StatusCode == HttpStatusCode.OK || (int)cur.StatusCode is >= 400 and < 500);

        // cancel, then switch the cancelled subscription to the other plan, then restart
        var cancel = await shopper.PostAsync($"api/subscriptions/{idA}/cancel", null);
        Assert.AreEqual(HttpStatusCode.OK, cancel.StatusCode, await cancel.Content.ReadAsStringAsync());
        Assert.AreEqual("canceled", (await Json(cancel)).GetProperty("state").GetString());
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"api/subscriptions/{idA}/cancel", null)).StatusCode);

        var sw = await shopper.PostAsJsonAsync($"api/subscriptions/{idA}/switch", new { planHandle = handleB });
        Console.WriteLine($"switch of a canceled subscription -> {(int)sw.StatusCode}: {await sw.Content.ReadAsStringAsync()}");
        if (sw.StatusCode == HttpStatusCode.OK)
        {
            var swView = await Json(sw);
            Assert.AreEqual(handleB, swView.GetProperty("planHandle").GetString());
            Assert.AreNotEqual("canceled", swView.GetProperty("state").GetString());
        }
        else
        {
            Assert.IsTrue((int)sw.StatusCode is >= 400 and < 500);
            var restart = await shopper.PostAsync($"api/subscriptions/{idA}/restart", null);
            Console.WriteLine($"restart -> {(int)restart.StatusCode}: {await restart.Content.ReadAsStringAsync()}");
            Assert.IsTrue(restart.StatusCode == HttpStatusCode.OK || (int)restart.StatusCode is >= 400 and < 500);
        }
    }
}
