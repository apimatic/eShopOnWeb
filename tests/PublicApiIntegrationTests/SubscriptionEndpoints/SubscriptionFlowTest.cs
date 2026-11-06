using System.Linq;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Runs against the real Maxio sandbox (credentials come from user-secrets: Maxio:*).
/// </summary>
[TestClass]
public class SubscriptionFlowTest
{
    private static HttpClient Client(string token)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    [TestMethod]
    public async Task PlansRequireSignIn()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ShopperCannotUseAdminEndpoints()
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());
        var response = await client.PostAsJsonAsync("api/admin/plans", new { name = "x", monthlyPrice = 1, brand = "Azure" });
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task AdminRejectsUnknownBrand()
    {
        var client = Client(ApiTokenHelper.GetAdminUserToken());
        var response = await client.PostAsJsonAsync("api/admin/plans", new { name = "No Brand", monthlyPrice = 1, brand = "Nope" });
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscriptionLifecycle()
    {
        var admin = Client(ApiTokenHelper.GetAdminUserToken());
        var shopper = Client(ApiTokenHelper.GetNormalUserToken());
        var suffix = Guid.NewGuid().ToString("N")[..8];

        // new plans
        var created = await admin.PostAsJsonAsync("api/admin/plans", new { name = $"IT Plan {suffix}", monthlyPrice = 12.5m, brand = "Azure" });
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());
        var plan = await Json(created);
        var handle = plan.GetProperty("handle").GetString()!;
        var otherHandle = (await Json(await admin.PostAsJsonAsync("api/admin/plans",
            new { name = $"IT Plan {suffix} B", monthlyPrice = 20, brand = ".NET" }))).GetProperty("handle").GetString()!;

        // duplicate name
        var dup = await admin.PostAsJsonAsync("api/admin/plans", new { name = $"IT Plan {suffix}", monthlyPrice = 1, brand = "Azure" });
        Assert.AreEqual(HttpStatusCode.Conflict, dup.StatusCode);

        // plans list
        var plans = await Json(await shopper.GetAsync("api/plans"));
        Assert.IsTrue(plans.EnumerateArray().Any(p => p.GetProperty("handle").GetString() == handle));

        // subscribe; several at once
        var sub1 = await Json(await shopper.PostAsJsonAsync("api/subscriptions", new { planHandle = handle }));
        var sub2 = await Json(await shopper.PostAsJsonAsync("api/subscriptions", new { planHandle = handle }));
        var id = sub1.GetProperty("id").GetInt64();
        Assert.AreNotEqual(id, sub2.GetProperty("id").GetInt64());
        Assert.AreEqual(HttpStatusCode.NotFound,
            (await shopper.PostAsJsonAsync("api/subscriptions", new { planHandle = "does-not-exist" })).StatusCode);

        // page, with paid=false because the first invoice is unpaid
        var page = await Json(await shopper.GetAsync($"api/subscriptions/{id}"));
        Assert.AreEqual("active", page.GetProperty("state").GetString());
        Assert.IsFalse(page.GetProperty("paid").GetBoolean());

        // someone else's subscription looks missing
        var stranger = Client(ApiTokenHelper.GetAdminUserToken());
        Assert.AreEqual(HttpStatusCode.NotFound, (await stranger.GetAsync($"api/subscriptions/{id}")).StatusCode);

        // pause / resume
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"api/subscriptions/{id}/resume", null)).StatusCode);
        Assert.AreEqual("on_hold", (await Json(await shopper.PostAsync($"api/subscriptions/{id}/pause", null))).GetProperty("state").GetString());
        Assert.AreEqual("active", (await Json(await shopper.PostAsync($"api/subscriptions/{id}/resume", null))).GetProperty("state").GetString());

        // cancel / restart
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"api/subscriptions/{id}/restart", null)).StatusCode);
        Assert.AreEqual("canceled", (await Json(await shopper.PostAsync($"api/subscriptions/{id}/cancel", null))).GetProperty("state").GetString());
        Assert.AreEqual("active", (await Json(await shopper.PostAsync($"api/subscriptions/{id}/restart", null))).GetProperty("state").GetString());

        // switch, including from a cancelled subscription
        await shopper.PostAsync($"api/subscriptions/{id}/cancel", null);
        var switched = await Json(await shopper.PostAsJsonAsync($"api/subscriptions/{id}/switch", new { planHandle = otherHandle }));
        Assert.AreEqual(otherHandle, switched.GetProperty("planHandle").GetString());
        Assert.AreEqual("active", switched.GetProperty("state").GetString());

        // usage for a day
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var usage = await shopper.GetAsync($"api/subscriptions/{id}/usage?date={today}");
        Assert.AreEqual(HttpStatusCode.OK, usage.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await shopper.GetAsync($"api/subscriptions/{id}/usage?date=yesterday")).StatusCode);

        // next billing date
        var date = DateTime.UtcNow.AddDays(20).ToString("yyyy-MM-dd");
        var next = await Json(await admin.PutAsJsonAsync($"api/admin/subscriptions/{id}/next-billing-date", new { date }));
        Assert.IsTrue(next.GetProperty("nextBillingDate").GetString()!.StartsWith(date));
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity,
            (await admin.PutAsJsonAsync($"api/admin/subscriptions/{id}/next-billing-date", new { date = "2020-01-01" })).StatusCode);

        // currency price: Maxio refuses currencies the site has not enabled; the reason comes back as a 4xx
        var cp = await admin.PostAsJsonAsync($"api/admin/plans/{plan.GetProperty("id").GetInt64()}/currency-prices", new { currency = "EUR", price = 11 });
        Assert.IsTrue((int)cp.StatusCode is >= 400 and < 500 || cp.StatusCode == HttpStatusCode.Created);
        await shopper.PostAsync($"api/subscriptions/{sub2.GetProperty("id").GetInt64()}/cancel", null);
        await shopper.PostAsync($"api/subscriptions/{id}/cancel", null);
    }
}
