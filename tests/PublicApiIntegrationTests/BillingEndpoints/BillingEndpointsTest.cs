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
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.BillingEndpoints;

/// <summary>
/// Runs against the real Maxio sandbox (settings from user-secrets). Nothing is stubbed.
/// </summary>
[TestClass]
public class BillingEndpointsTest
{
    private static readonly string Shopper = "it-" + Guid.NewGuid().ToString("N")[..10] + "@example.com";
    private static readonly string Stranger = "it-" + Guid.NewGuid().ToString("N")[..10] + "@example.com";

    private static HttpClient As(string user, bool admin = false)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetUserToken(user, admin));
        return client;
    }

    private static async Task<JsonNode> Json(HttpResponseMessage r) => JsonNode.Parse(await r.Content.ReadAsStringAsync())!;

    private static HttpClient MaxioDirect()
    {
        var cfg = new ConfigurationBuilder().AddUserSecrets<Program>().Build();
        var http = new HttpClient { BaseAddress = new Uri("https://" + cfg["Maxio:Subdomain"] + ".chargify.com/") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes(cfg["Maxio:ApiKey"] + ":x")));
        return http;
    }

    private static async Task<(long id, string handle)> NewPlan(string name, decimal price)
    {
        var admin = As("admin@microsoft.com", true);
        var r = await admin.PostAsJsonAsync("api/admin/plans", new { name, monthlyPrice = price, brand = "Azure" });
        Assert.AreEqual(HttpStatusCode.Created, r.StatusCode, await r.Content.ReadAsStringAsync());
        var j = await Json(r);
        return ((long)j["id"]!, (string)j["handle"]!);
    }

    private static async Task<long> Subscribe(HttpClient client, string handle)
    {
        var r = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = handle });
        Assert.AreEqual(HttpStatusCode.Created, r.StatusCode, await r.Content.ReadAsStringAsync());
        return (long)(await Json(r))["id"]!;
    }

    [TestMethod]
    public async Task EndpointsRequireAuthenticationAndStaffEndpointsRequireAdmin()
    {
        var anon = ProgramTest.NewClient;
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anon.GetAsync("api/plans")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anon.GetAsync("api/subscriptions/1")).StatusCode);

        var shopper = As(Shopper);
        var r = await shopper.PostAsJsonAsync("api/admin/plans", new { name = "x", monthlyPrice = 1, brand = "Azure" });
        Assert.AreEqual(HttpStatusCode.Forbidden, r.StatusCode);
        r = await shopper.PutAsJsonAsync("api/admin/subscriptions/1/next-billing-date", new { date = "2030-01-01" });
        Assert.AreEqual(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [TestMethod]
    public async Task ShopperLifecycle()
    {
        var name = "IT Plan " + Guid.NewGuid().ToString("N")[..8];
        var (planId, handle) = await NewPlan(name, 12.50m);
        var (_, handle2) = await NewPlan(name + " B", 20m);

        var shopper = As(Shopper);

        // plans
        var plans = await Json(await shopper.GetAsync("api/plans"));
        Assert.IsTrue(plans["plans"]!.AsArray().Any(p => (string?)p!["handle"] == handle));

        // subscribe to two plans at once, and an unknown plan
        var s1 = await Subscribe(shopper, handle);
        var s2 = await Subscribe(shopper, handle2);
        Assert.AreNotEqual(s1, s2);
        Assert.AreEqual(HttpStatusCode.NotFound,
            (await shopper.PostAsJsonAsync("api/subscriptions", new { planHandle = "no-such-plan-xyz" })).StatusCode);

        // subscription page + paid
        var page = await Json(await shopper.GetAsync("api/subscriptions/" + s1));
        Assert.AreEqual("active", (string?)page["state"]);
        Assert.AreEqual(handle, (string?)page["planHandle"]);
        Assert.AreEqual(false, (bool)page["paid"]!); // an unpaid invoice is open

        // another shopper cannot see it or act on it
        var stranger = As(Stranger);
        Assert.AreEqual(HttpStatusCode.NotFound, (await stranger.GetAsync("api/subscriptions/" + s1)).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await stranger.PostAsync("api/subscriptions/" + s1 + "/cancel", null)).StatusCode);

        // pause / resume
        var r = await shopper.PostAsync("api/subscriptions/" + s1 + "/pause", null);
        Assert.AreEqual("on_hold", (string?)(await Json(r))["state"]);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, (await shopper.PostAsync("api/subscriptions/" + s1 + "/pause", null)).StatusCode);
        r = await shopper.PostAsync("api/subscriptions/" + s1 + "/resume", null);
        Assert.AreEqual("active", (string?)(await Json(r))["state"]);

        // switch plan while paused: ends up on new plan, still paused
        await shopper.PostAsync("api/subscriptions/" + s1 + "/pause", null);
        r = await shopper.PostAsJsonAsync("api/subscriptions/" + s1 + "/switch", new { planHandle = handle2 });
        Assert.AreEqual(HttpStatusCode.OK, r.StatusCode, await r.Content.ReadAsStringAsync());
        var j = await Json(r);
        Assert.AreEqual(handle2, (string?)j["planHandle"]);
        Assert.AreEqual("on_hold", (string?)j["state"]);
        await shopper.PostAsync("api/subscriptions/" + s1 + "/resume", null);

        // cancel / restart / switch a cancelled subscription
        r = await shopper.PostAsync("api/subscriptions/" + s1 + "/cancel", null);
        Assert.AreEqual("canceled", (string?)(await Json(r))["state"]);
        r = await shopper.PostAsJsonAsync("api/subscriptions/" + s1 + "/switch", new { planHandle = handle });
        Assert.AreEqual(HttpStatusCode.OK, r.StatusCode, await r.Content.ReadAsStringAsync());
        j = await Json(r);
        Assert.AreEqual(handle, (string?)j["planHandle"]);
        Assert.AreEqual("active", (string?)j["state"]);
        await shopper.PostAsync("api/subscriptions/" + s1 + "/cancel", null);
        r = await shopper.PostAsync("api/subscriptions/" + s1 + "/restart", null);
        Assert.AreEqual("active", (string?)(await Json(r))["state"]);

        // admin: next billing date
        var admin = As("admin@microsoft.com", true);
        var date = DateTime.UtcNow.Date.AddDays(40);
        r = await admin.PutAsJsonAsync("api/admin/subscriptions/" + s2 + "/next-billing-date", new { date = date.ToString("yyyy-MM-dd") });
        Assert.AreEqual(HttpStatusCode.OK, r.StatusCode, await r.Content.ReadAsStringAsync());
        j = await Json(r);
        Assert.AreEqual(date.Date, DateTimeOffset.Parse((string)j["nextBillingDate"]!).Date);
        Assert.AreEqual(HttpStatusCode.BadRequest,
            (await admin.PutAsJsonAsync("api/admin/subscriptions/" + s2 + "/next-billing-date", new { date = "2001-01-01" })).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest,
            (await admin.PutAsJsonAsync("api/admin/subscriptions/" + s2 + "/next-billing-date", new { date = "nope" })).StatusCode);

        // admin: currency price (the sandbox site has only USD, so Maxio refuses; must be a 4xx with a reason)
        r = await admin.PostAsJsonAsync("api/admin/plans/" + planId + "/currency-prices", new { currency = "EUR", price = 11.0 });
        Assert.IsTrue((int)r.StatusCode is >= 400 and < 500, r.StatusCode + await r.Content.ReadAsStringAsync());
        StringAssert.Contains(await r.Content.ReadAsStringAsync(), "Message");
        r = await admin.PostAsJsonAsync("api/admin/plans/999999999/currency-prices", new { currency = "EUR", price = 11.0 });
        Assert.AreEqual(HttpStatusCode.NotFound, r.StatusCode);

        // admin: bad plans
        r = await admin.PostAsJsonAsync("api/admin/plans", new { name = "Bad Brand", monthlyPrice = 5, brand = "NoSuchBrand" });
        Assert.AreEqual(HttpStatusCode.BadRequest, r.StatusCode);
        r = await admin.PostAsJsonAsync("api/admin/plans", new { name, monthlyPrice = 5, brand = "Azure" });
        Assert.AreEqual(HttpStatusCode.Conflict, r.StatusCode);
    }

    [TestMethod]
    public async Task UsageForASingleDay()
    {
        var shopper = As(Shopper);
        var plans = await Json(await shopper.GetAsync("api/plans"));
        var metered = plans["plans"]!.AsArray().First(p => (string?)p!["handle"] == "basic-plan");
        var sub = await Subscribe(shopper, (string)metered!["handle"]!);

        // Another system records usage directly in Maxio.
        var maxio = MaxioDirect();
        var comps = JsonNode.Parse(await maxio.GetStringAsync("subscriptions/" + sub + "/components.json"))!.AsArray();
        var component = comps.First(c => (string?)c!["component"]!["kind"] == "metered_component")!["component"]!;
        var cid = (long)component["component_id"]!;
        foreach (var q in new[] { 5, 7 })
        {
            var rr = await maxio.PostAsJsonAsync("subscriptions/" + sub + "/components/" + cid + "/usages.json",
                new { usage = new { quantity = q, memo = "integration test" } });
            rr.EnsureSuccessStatusCode();
        }

        var today = DateTime.UtcNow.Date;
        // Maxio records the usage on the site's local calendar day, which may differ from UTC's.
        var found = 0m;
        foreach (var d in new[] { today.AddDays(-1), today, today.AddDays(1) })
        {
            var r = await shopper.GetAsync("api/subscriptions/" + sub + "/usage?date=" + d.ToString("yyyy-MM-dd"));
            Assert.AreEqual(HttpStatusCode.OK, r.StatusCode);
            var j = await Json(r);
            found += j["components"]!.AsArray().Sum(c => (decimal)c!["quantity"]!);
        }
        Assert.AreEqual(12m, found);

        var far = await Json(await shopper.GetAsync("api/subscriptions/" + sub + "/usage?date=2020-01-01"));
        Assert.AreEqual(0m, far["components"]!.AsArray().Sum(c => (decimal)c!["quantity"]!));

        Assert.AreEqual(HttpStatusCode.BadRequest, (await shopper.GetAsync("api/subscriptions/" + sub + "/usage?date=01/02/2020")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound,
            (await As(Stranger).GetAsync("api/subscriptions/" + sub + "/usage?date=2020-01-01")).StatusCode);
    }
}
