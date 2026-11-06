using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.BillingEndpoints;

/// <summary>
/// Runs against the real Maxio sandbox. Credentials come from user-secrets (Maxio:*) for the API under test
/// and from the MAXIO_* environment variables for the test's own "another system records usage" calls.
/// </summary>
[TestClass]
public class BillingEndpointsTest
{
    private static readonly string Run = Guid.NewGuid().ToString("N")[..8];
    private static readonly string Shopper = $"billing-test-{Run}@example.com";
    private static readonly string Other = $"billing-other-{Run}@example.com";

    private static HttpClient Client(string? token)
    {
        var client = ProgramTest.NewClient;
        if (token != null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static HttpClient ShopperClient(string? name = null) => Client(ApiTokenHelper.GetUserToken(name ?? Shopper));
    private static HttpClient AdminClient() => Client(ApiTokenHelper.GetAdminUserToken());

    private static StringContent Json(object o) => new(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");

    private static async Task<JsonNode> Body(HttpResponseMessage r)
    {
        var text = await r.Content.ReadAsStringAsync();
        Assert.IsTrue(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {text}");
        return JsonNode.Parse(text)!;
    }

    private static async Task<string> FirstPlanHandle(string? not = null)
    {
        var plans = await Body(await ShopperClient().GetAsync("api/plans"));
        foreach (var p in plans["plans"]!.AsArray())
        {
            var handle = p!["handle"]!.GetValue<string>();
            if (handle != not && p["priceInCents"]!.GetValue<long>() > 0) return handle;
        }
        Assert.Fail("No plan available");
        return "";
    }

    private static async Task<long> Subscribe(string handle, string? who = null)
    {
        var r = await ShopperClient(who).PostAsync("api/subscriptions", Json(new { planHandle = handle }));
        Assert.AreEqual(HttpStatusCode.Created, r.StatusCode, await r.Content.ReadAsStringAsync());
        return (await Body(r))["subscription"]!["id"]!.GetValue<long>();
    }

    private static async Task<JsonNode> Get(long id, string? who = null) =>
        (await Body(await ShopperClient(who).GetAsync($"api/subscriptions/{id}")))["subscription"]!;

    [TestMethod]
    public async Task RequiresAuthentication()
    {
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await Client(null).GetAsync("api/plans")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await Client(null).PostAsync("api/subscriptions", Json(new { planHandle = "x" }))).StatusCode);
    }

    [TestMethod]
    public async Task StaffEndpointsAreForbiddenForShoppers()
    {
        var c = ShopperClient();
        Assert.AreEqual(HttpStatusCode.Forbidden, (await c.PostAsync("api/admin/plans", Json(new { name = "x", monthlyPrice = 1, catalogBrandId = 1 }))).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await c.PostAsync("api/admin/plans/1/currency-prices", Json(new { currency = "EUR", price = 1 }))).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await c.PutAsync("api/admin/subscriptions/1/next-billing-date", Json(new { date = "2030-01-01" }))).StatusCode);
    }

    [TestMethod]
    public async Task SubscribeToSeveralPlansPauseResumeCancelRestartAndSwitch()
    {
        var planA = await FirstPlanHandle();
        var planB = await FirstPlanHandle(planA);

        var a = await Subscribe(planA);
        var b = await Subscribe(planB);
        Assert.AreNotEqual(a, b);

        var sub = await Get(a);
        Assert.AreEqual("active", sub["state"]!.GetValue<string>());
        Assert.AreEqual(planA, sub["planHandle"]!.GetValue<string>());
        Assert.IsFalse(sub["paid"]!.GetValue<bool>(), "a freshly invoiced plan is owed");

        var c = ShopperClient();
        Assert.AreEqual("on_hold", (await Body(await c.PostAsync($"api/subscriptions/{a}/pause", null)))["subscription"]!["state"]!.GetValue<string>());
        Assert.AreEqual("active", (await Body(await c.PostAsync($"api/subscriptions/{a}/resume", null)))["subscription"]!["state"]!.GetValue<string>());

        // Not valid transitions: reason + 4xx
        var bad = await c.PostAsync($"api/subscriptions/{a}/resume", null);
        Assert.IsTrue((int)bad.StatusCode is >= 400 and < 500);
        Assert.IsFalse(string.IsNullOrWhiteSpace(await bad.Content.ReadAsStringAsync()));
        Assert.IsTrue((int)(await c.PostAsync($"api/subscriptions/{a}/restart", null)).StatusCode is >= 400 and < 500);

        Assert.AreEqual("canceled", (await Body(await c.PostAsync($"api/subscriptions/{a}/cancel", null)))["subscription"]!["state"]!.GetValue<string>());
        Assert.IsTrue((int)(await c.PostAsync($"api/subscriptions/{a}/cancel", null)).StatusCode is >= 400 and < 500);

        // Switch a cancelled subscription: it ends up active on the new plan.
        var switched = (await Body(await c.PostAsync($"api/subscriptions/{a}/switch", Json(new { planHandle = planB }))))["subscription"]!;
        Assert.AreEqual(planB, switched["planHandle"]!.GetValue<string>());
        Assert.AreEqual("active", switched["state"]!.GetValue<string>());

        // Switch an active subscription.
        var back = (await Body(await c.PostAsync($"api/subscriptions/{a}/switch", Json(new { planHandle = planA }))))["subscription"]!;
        Assert.AreEqual(planA, back["planHandle"]!.GetValue<string>());

        // Restart
        await c.PostAsync($"api/subscriptions/{b}/cancel", null);
        Assert.AreEqual("active", (await Body(await c.PostAsync($"api/subscriptions/{b}/restart", null)))["subscription"]!["state"]!.GetValue<string>());

        Assert.AreEqual(HttpStatusCode.NotFound, (await c.PostAsync($"api/subscriptions/{a}/switch", Json(new { planHandle = "no-such-plan" }))).StatusCode);
    }

    [TestMethod]
    public async Task ShopperCannotSeeOrTouchSomeoneElsesSubscription()
    {
        var id = await Subscribe(await FirstPlanHandle());
        var other = ShopperClient(Other);
        Assert.AreEqual(HttpStatusCode.NotFound, (await other.GetAsync($"api/subscriptions/{id}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await other.PostAsync($"api/subscriptions/{id}/cancel", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await other.GetAsync($"api/subscriptions/{id}/usage?date=2026-01-01")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await ShopperClient().GetAsync("api/subscriptions/1")).StatusCode);
    }

    [TestMethod]
    public async Task SubscribeValidatesThePlan()
    {
        var c = ShopperClient();
        Assert.AreEqual(HttpStatusCode.BadRequest, (await c.PostAsync("api/subscriptions", Json(new { }))).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await c.PostAsync("api/subscriptions", Json(new { planHandle = "does-not-exist" }))).StatusCode);
    }

    [TestMethod]
    public async Task UsageForADay()
    {
        var id = await Subscribe(await FirstPlanHandle());
        var key = Environment.GetEnvironmentVariable("MAXIO_API_KEY");
        var sub = Environment.GetEnvironmentVariable("MAXIO_SITE_SUBDOMAIN");
        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(sub)) Assert.Inconclusive("MAXIO_* environment variables are not set.");

        using var maxio = new HttpClient { BaseAddress = new Uri($"https://{sub}.chargify.com/") };
        maxio.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(key + ":x")));
        var comps = JsonNode.Parse(await maxio.GetStringAsync($"subscriptions/{id}/components.json"))!.AsArray();
        var meteredId = comps.First(x => x!["component"]!["kind"]!.GetValue<string>() == "metered_component")!["component"]!["component_id"]!.GetValue<long>();
        foreach (var q in new[] { 5, 3 })
        {
            var r = await maxio.PostAsJsonAsync($"subscriptions/{id}/components/{meteredId}/usages.json", new { usage = new { quantity = q, memo = "test" } });
            r.EnsureSuccessStatusCode();
        }

        var today = DateTime.UtcNow.AddHours(5).ToString("yyyy-MM-dd"); // sandbox site time is UTC+5
        var c = ShopperClient();
        var usage = await Body(await c.GetAsync($"api/subscriptions/{id}/usage?date={today}"));
        Assert.AreEqual(8m, usage["totalQuantity"]!.GetValue<decimal>());

        var other = await Body(await c.GetAsync($"api/subscriptions/{id}/usage?date=2020-01-01"));
        Assert.AreEqual(0m, other["totalQuantity"]!.GetValue<decimal>());

        Assert.AreEqual(HttpStatusCode.BadRequest, (await c.GetAsync($"api/subscriptions/{id}/usage")).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await c.GetAsync($"api/subscriptions/{id}/usage?date=nope")).StatusCode);
    }

    [TestMethod]
    public async Task StaffCreatePlanThenSetNextBillingDate()
    {
        var admin = AdminClient();
        var brands = await Body(await admin.GetAsync("api/catalog-brands"));
        var brandId = brands["catalogBrands"]![0]!["id"]!.GetValue<int>();
        var brandName = brands["catalogBrands"]![0]!["name"]!.GetValue<string>();

        Assert.AreEqual(HttpStatusCode.BadRequest, (await admin.PostAsync("api/admin/plans", Json(new { name = $"T {Run}", monthlyPrice = 5, catalogBrandId = 99999 }))).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await admin.PostAsync("api/admin/plans", Json(new { name = $"T {Run}", monthlyPrice = -5, catalogBrandId = brandId }))).StatusCode);

        var created = await admin.PostAsync("api/admin/plans", Json(new { name = $"Test Plan {Run}", monthlyPrice = 12.34, catalogBrandId = brandId }));
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());
        var plan = (await Body(created))["plan"]!;
        Assert.AreEqual(1234, plan["priceInCents"]!.GetValue<long>());
        Assert.AreEqual(brandName, plan["brand"]!.GetValue<string>());

        var listed = await Body(await ShopperClient().GetAsync("api/plans"));
        Assert.IsTrue(listed["plans"]!.AsArray().Any(p => p!["handle"]!.GetValue<string>() == plan["handle"]!.GetValue<string>()));

        // Currency price: the sandbox site may have no extra currency enabled; then we must get a 4xx with a reason.
        var cp = await admin.PostAsync($"api/admin/plans/{plan["id"]}/currency-prices", Json(new { currency = "EUR", price = 11 }));
        Assert.IsTrue(cp.StatusCode == HttpStatusCode.Created || (int)cp.StatusCode is >= 400 and < 500, await cp.Content.ReadAsStringAsync());
        Assert.AreEqual(HttpStatusCode.NotFound, (await admin.PostAsync("api/admin/plans/1/currency-prices", Json(new { currency = "EUR", price = 11 }))).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await admin.PostAsync($"api/admin/plans/{plan["id"]}/currency-prices", Json(new { currency = "EURO", price = 11 }))).StatusCode);

        var id = await Subscribe(plan["handle"]!.GetValue<string>());
        var date = DateTime.UtcNow.AddDays(40).ToString("yyyy-MM-dd");
        var set = (await Body(await admin.PutAsync($"api/admin/subscriptions/{id}/next-billing-date", Json(new { date }))))["subscription"]!;
        Assert.AreEqual(date, set["nextBillingAt"]!.GetValue<string>()[..10]);
        Assert.AreEqual(date, (await Get(id))["nextBillingAt"]!.GetValue<string>()[..10]);

        Assert.AreEqual(HttpStatusCode.BadRequest, (await admin.PutAsync($"api/admin/subscriptions/{id}/next-billing-date", Json(new { }))).StatusCode);
        Assert.IsTrue((int)(await admin.PutAsync($"api/admin/subscriptions/{id}/next-billing-date", Json(new { date = "2001-01-01" }))).StatusCode is >= 400 and < 500);
        Assert.AreEqual(HttpStatusCode.NotFound, (await admin.PutAsync("api/admin/subscriptions/1/next-billing-date", Json(new { date }))).StatusCode);

        await ShopperClient().PostAsync($"api/subscriptions/{id}/cancel", null);
        Assert.IsTrue((int)(await admin.PutAsync($"api/admin/subscriptions/{id}/next-billing-date", Json(new { date }))).StatusCode is >= 400 and < 500);
    }
}
