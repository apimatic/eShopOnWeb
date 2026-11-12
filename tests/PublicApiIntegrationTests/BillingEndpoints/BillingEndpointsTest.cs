using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.BillingEndpoints;

/// <summary>
/// These tests call the real Maxio sandbox site configured through the Maxio:* settings (user-secrets).
/// Nothing is stubbed.
/// </summary>
[TestClass]
public class BillingEndpointsTest
{
    private static HttpClient Client(string token)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    [TestMethod]
    public async Task ShopperEndpointsRequireAToken()
    {
        var client = ProgramTest.NewClient;
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/plans")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/subscriptions", new { planHandle = "x" })).StatusCode);
    }

    [TestMethod]
    public async Task StaffEndpointsAreForAdministrators()
    {
        var shopper = Client(ApiTokenHelper.GetNormalUserToken());
        var plan = await shopper.PostAsJsonAsync("/api/admin/plans", new { name = "x", monthlyPrice = 1, brand = "Azure" });
        Assert.AreEqual(HttpStatusCode.Forbidden, plan.StatusCode);
        var price = await shopper.PostAsJsonAsync("/api/admin/plans/1/currency-prices", new { currency = "EUR", price = 1 });
        Assert.AreEqual(HttpStatusCode.Forbidden, price.StatusCode);
        var date = await shopper.PutAsJsonAsync("/api/admin/subscriptions/1/next-billing-date", new { date = "2099-01-01" });
        Assert.AreEqual(HttpStatusCode.Forbidden, date.StatusCode);
    }

    [TestMethod]
    public async Task RejectsRequestsTheAppCannotCarryOut()
    {
        var admin = Client(ApiTokenHelper.GetAdminUserToken());
        var shopper = Client(ApiTokenHelper.GetNormalUserToken());

        var unknownBrand = await admin.PostAsJsonAsync("/api/admin/plans", new { name = "Whatever", monthlyPrice = 5, brand = "No Such Brand" });
        Assert.AreEqual(HttpStatusCode.BadRequest, unknownBrand.StatusCode);
        var freePlan = await admin.PostAsJsonAsync("/api/admin/plans", new { name = "Whatever", monthlyPrice = 0, brand = "Azure" });
        Assert.AreEqual(HttpStatusCode.BadRequest, freePlan.StatusCode);
        var pastDate = await admin.PutAsJsonAsync("/api/admin/subscriptions/1/next-billing-date", new { date = "2001-01-01" });
        Assert.AreEqual(HttpStatusCode.BadRequest, pastDate.StatusCode);
        var badDate = await admin.PutAsJsonAsync("/api/admin/subscriptions/1/next-billing-date", new { date = "soon" });
        Assert.AreEqual(HttpStatusCode.BadRequest, badDate.StatusCode);

        var notOffered = await shopper.PostAsJsonAsync("/api/subscriptions", new { planHandle = "no-such-plan-handle" });
        Assert.AreEqual(HttpStatusCode.NotFound, notOffered.StatusCode);
        var noHandle = await shopper.PostAsJsonAsync("/api/subscriptions", new { });
        Assert.AreEqual(HttpStatusCode.BadRequest, noHandle.StatusCode);
        var missingSub = await shopper.GetAsync("/api/subscriptions/1");
        Assert.AreEqual(HttpStatusCode.NotFound, missingSub.StatusCode);
        var badUsageDate = await shopper.GetAsync("/api/subscriptions/1/usage?date=11/12/2026");
        Assert.AreEqual(HttpStatusCode.BadRequest, badUsageDate.StatusCode);
        var missingPlan = await admin.PostAsJsonAsync("/api/admin/plans/1/currency-prices", new { currency = "EUR", price = 10 });
        Assert.AreEqual(HttpStatusCode.NotFound, missingPlan.StatusCode);
    }

    [TestMethod]
    public async Task SubscriptionLifecycleAgainstTheSandbox()
    {
        var admin = Client(ApiTokenHelper.GetAdminUserToken());
        var shopper = Client(ApiTokenHelper.GetNormalUserToken());
        var other = Client(ApiTokenHelper.GetAdminUserToken()); // another identity: not the subscriber

        var suffix = Guid.NewGuid().ToString("N")[..8];
        async Task<JsonElement> NewPlan(string name, decimal price, string brand)
        {
            var created = await admin.PostAsJsonAsync("/api/admin/plans", new { name, monthlyPrice = price, brand });
            Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());
            return await Json(created);
        }

        var basic = await NewPlan($"IT Basic {suffix}", 10m, "Azure");
        var pro = await NewPlan($"IT Pro {suffix}", 25m, ".NET");
        Assert.AreEqual(1000, basic.GetProperty("priceInCents").GetInt32());
        var basicHandle = basic.GetProperty("handle").GetString()!;
        var proHandle = pro.GetProperty("handle").GetString()!;

        // a duplicate plan is refused
        var duplicate = await admin.PostAsJsonAsync("/api/admin/plans", new { name = $"IT Basic {suffix}", monthlyPrice = 10, brand = "Azure" });
        Assert.AreEqual(HttpStatusCode.Conflict, duplicate.StatusCode);

        // plans are listed
        var plans = await Json(await shopper.GetAsync("/api/plans"));
        var handles = new List<string>();
        foreach (var p in plans.GetProperty("plans").EnumerateArray()) handles.Add(p.GetProperty("handle").GetString()!);
        CollectionAssert.IsSubsetOf(new[] { basicHandle, proHandle }, handles);

        // subscribe to two plans at once, without card details
        var first = await shopper.PostAsJsonAsync("/api/subscriptions", new { planHandle = basicHandle });
        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode, await first.Content.ReadAsStringAsync());
        var sub = await Json(first);
        var id = sub.GetProperty("id").GetInt32();
        Assert.AreEqual(basicHandle, sub.GetProperty("planHandle").GetString());
        var second = await shopper.PostAsJsonAsync("/api/subscriptions", new { planHandle = proHandle });
        Assert.AreEqual(HttpStatusCode.Created, second.StatusCode, await second.Content.ReadAsStringAsync());
        var secondId = (await Json(second)).GetProperty("id").GetInt32();
        Assert.AreNotEqual(id, secondId);

        // the same plan twice is refused
        var again = await shopper.PostAsJsonAsync("/api/subscriptions", new { planHandle = basicHandle });
        Assert.AreEqual(HttpStatusCode.Conflict, again.StatusCode);

        // the subscription page: money is owed, so paid is false
        var page = await Json(await shopper.GetAsync($"/api/subscriptions/{id}"));
        Assert.AreEqual(1000, page.GetProperty("balanceInCents").GetInt64());
        Assert.IsFalse(page.GetProperty("paid").GetBoolean());

        // somebody else's subscription looks like a missing one
        Assert.AreEqual(HttpStatusCode.NotFound, (await other.GetAsync($"/api/subscriptions/{id}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await other.PostAsync($"/api/subscriptions/{id}/cancel", null)).StatusCode);

        // pause and resume, with the state rules
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"/api/subscriptions/{id}/resume", null)).StatusCode);
        var paused = await Json(await shopper.PostAsync($"/api/subscriptions/{id}/pause", null));
        Assert.AreEqual("on_hold", paused.GetProperty("state").GetString());
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"/api/subscriptions/{id}/pause", null)).StatusCode);
        var resumed = await Json(await shopper.PostAsync($"/api/subscriptions/{id}/resume", null));
        Assert.AreEqual("active", resumed.GetProperty("state").GetString());

        // cancel and restart
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"/api/subscriptions/{id}/restart", null)).StatusCode);
        var canceled = await Json(await shopper.PostAsync($"/api/subscriptions/{id}/cancel", null));
        Assert.AreEqual("canceled", canceled.GetProperty("state").GetString());

        // switching a canceled subscription puts it on the new plan
        var switched = await shopper.PostAsJsonAsync($"/api/subscriptions/{id}/switch", new { planHandle = proHandle });
        Assert.AreEqual(HttpStatusCode.OK, switched.StatusCode, await switched.Content.ReadAsStringAsync());
        Assert.AreEqual(proHandle, (await Json(switched)).GetProperty("planHandle").GetString());
        Assert.AreEqual(HttpStatusCode.Conflict,
            (await shopper.PostAsJsonAsync($"/api/subscriptions/{id}/switch", new { planHandle = proHandle })).StatusCode);

        var restarted = await Json(await shopper.PostAsync($"/api/subscriptions/{id}/restart", null));
        Assert.AreEqual("active", restarted.GetProperty("state").GetString());

        // usage for a day
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var usage = await shopper.GetAsync($"/api/subscriptions/{id}/usage?date={today}");
        Assert.AreEqual(HttpStatusCode.OK, usage.StatusCode, await usage.Content.ReadAsStringAsync());
        Assert.IsFalse((await Json(usage)).GetProperty("truncated").GetBoolean());

        // staff: next billing date
        var when = DateTime.UtcNow.Date.AddDays(40);
        var nbd = await admin.PutAsJsonAsync($"/api/admin/subscriptions/{secondId}/next-billing-date", new { date = when.ToString("yyyy-MM-dd") });
        Assert.AreEqual(HttpStatusCode.OK, nbd.StatusCode, await nbd.Content.ReadAsStringAsync());
        var next = (await Json(nbd)).GetProperty("nextBillingAt").GetDateTimeOffset();
        Assert.AreEqual(when.Date, next.Date);

        // staff: another currency the site has not enabled is refused with a reason, not sent on
        var planId = basic.GetProperty("id").GetInt32();
        var currency = await admin.PostAsJsonAsync($"/api/admin/plans/{planId}/currency-prices", new { currency = "XTS", price = 10 });
        Assert.AreEqual(HttpStatusCode.BadRequest, currency.StatusCode);
        StringAssert.Contains(await currency.Content.ReadAsStringAsync(), "not an additional currency");

        // clean up the sandbox
        await shopper.PostAsync($"/api/subscriptions/{id}/cancel", null);
        await shopper.PostAsync($"/api/subscriptions/{secondId}/cancel", null);
    }

    [TestMethod]
    public async Task AnUnreachableBillingSystemIsAnswered502()
    {
        // Maxio:BaseUrl overrides the address verbatim; nothing listens on this port.
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["Maxio:BaseUrl"] = "http://127.0.0.1:9" })));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync("/api/plans");
        Assert.AreEqual(HttpStatusCode.BadGateway, response.StatusCode);
    }
}
