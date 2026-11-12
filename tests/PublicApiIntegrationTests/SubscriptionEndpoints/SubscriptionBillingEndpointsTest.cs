using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// These talk to the real Maxio sandbox (credentials come from the Maxio: settings / MAXIO_* variables);
/// nothing is stubbed.
/// </summary>
[TestClass]
public class SubscriptionBillingEndpointsTest
{
    private static HttpClient Client(string token)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static HttpClient Shopper() => Client(ApiTokenHelper.GetNormalUserToken());
    private static HttpClient Admin() => Client(ApiTokenHelper.GetAdminUserToken());

    private static async Task<JsonElement> Json(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    [TestMethod]
    public async Task PlansRequireSignIn()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task StaffRoutesAreForbiddenForShoppers()
    {
        var shopper = Shopper();
        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.PostAsJsonAsync("api/admin/plans", new { name = "x", price = 1, brand = "Azure" })).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.PostAsJsonAsync("api/admin/plans/1/currency-prices", new { currency = "EUR", price = 1 })).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.PutAsJsonAsync("api/admin/subscriptions/1/next-billing-date", new { date = "2030-01-01" })).StatusCode);
    }

    [TestMethod]
    public async Task NewPlanMustUseACatalogBrand()
    {
        var response = await Admin().PostAsJsonAsync("api/admin/plans", new { name = "Ghost brand plan", price = 5, brand = "No Such Brand" });
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeRejectsAPlanThatIsNotOnOffer()
    {
        var response = await Shopper().PostAsJsonAsync("api/subscriptions", new { planHandle = "no-such-plan-handle" });
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task UsageRejectsABadDate()
    {
        var response = await Shopper().GetAsync("api/subscriptions/1/usage?date=yesterday");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task ShopperCannotSeeSomeoneElsesSubscription()
    {
        var admin = Admin();
        var plan = await CreatePlan(admin, "Own", 7m);
        var created = await Shopper().PostAsJsonAsync("api/subscriptions", new { planHandle = plan });
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        var id = (await Json(created)).GetProperty("id").GetInt32();

        // the administrator is a different customer in Maxio, so the shopper's subscription is not theirs
        Assert.AreEqual(HttpStatusCode.NotFound, (await admin.GetAsync($"api/subscriptions/{id}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await admin.PostAsync($"api/subscriptions/{id}/cancel", null)).StatusCode);
        await Shopper().PostAsync($"api/subscriptions/{id}/cancel", null);
    }

    [TestMethod]
    public async Task SubscriptionLifecycleAgainstSandbox()
    {
        var admin = Admin();
        var shopper = Shopper();
        var planA = await CreatePlan(admin, "Lifecycle A", 12.5m);
        var planB = await CreatePlan(admin, "Lifecycle B", 0m);

        // the new plans are on offer
        var plans = await Json(await shopper.GetAsync("api/plans"));
        var handles = plans.GetProperty("plans").EnumerateArray().Select(p => p.GetProperty("handle").GetString()).ToList();
        CollectionAssert.IsSubsetOf(new[] { planA, planB }, handles);

        // subscribe (no card), owes money, so not paid
        var created = await shopper.PostAsJsonAsync("api/subscriptions", new { planHandle = planA });
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        var sub = await Json(created);
        var id = sub.GetProperty("id").GetInt32();
        Assert.IsFalse(sub.GetProperty("paid").GetBoolean());
        Assert.AreEqual(planA, sub.GetProperty("plan").GetProperty("handle").GetString());

        // the same plan twice is refused, a second plan is fine
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsJsonAsync("api/subscriptions", new { planHandle = planA })).StatusCode);
        var second = await shopper.PostAsJsonAsync("api/subscriptions", new { planHandle = planB });
        Assert.AreEqual(HttpStatusCode.Created, second.StatusCode);
        Assert.IsTrue((await Json(second)).GetProperty("paid").GetBoolean(), "a free plan owes nothing");

        // pause / resume
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"api/subscriptions/{id}/resume", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await shopper.PostAsync($"api/subscriptions/{id}/pause", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"api/subscriptions/{id}/pause", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await shopper.PostAsync($"api/subscriptions/{id}/resume", null)).StatusCode);

        // cancel, switch while cancelled, restart
        Assert.AreEqual(HttpStatusCode.OK, (await shopper.PostAsync($"api/subscriptions/{id}/cancel", null)).StatusCode);
        var switched = await shopper.PostAsJsonAsync($"api/subscriptions/{id}/switch", new { planHandle = planB });
        Assert.AreEqual(HttpStatusCode.OK, switched.StatusCode);
        Assert.AreEqual(planB, (await Json(switched)).GetProperty("plan").GetProperty("handle").GetString());
        Assert.AreEqual(HttpStatusCode.OK, (await shopper.PostAsync($"api/subscriptions/{id}/restart", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"api/subscriptions/{id}/restart", null)).StatusCode);

        // switch an active subscription
        var back = await shopper.PostAsJsonAsync($"api/subscriptions/{id}/switch", new { planHandle = planA });
        Assert.AreEqual(HttpStatusCode.OK, back.StatusCode);
        Assert.AreEqual(planA, (await Json(back)).GetProperty("plan").GetProperty("handle").GetString());

        // usage on a day with nothing recorded
        var usage = await shopper.GetAsync($"api/subscriptions/{id}/usage?date={DateTime.UtcNow:yyyy-MM-dd}");
        Assert.AreEqual(HttpStatusCode.OK, usage.StatusCode);
        Assert.AreEqual(0m, (await Json(usage)).GetProperty("totalQuantity").GetDecimal());

        // staff set the next billing date
        var date = DateTime.UtcNow.Date.AddDays(40);
        var set = await admin.PutAsJsonAsync($"api/admin/subscriptions/{id}/next-billing-date", new { date = date.ToString("yyyy-MM-dd") });
        Assert.AreEqual(HttpStatusCode.OK, set.StatusCode);
        Assert.AreEqual(date.ToString("yyyy-MM-dd"), (await Json(set)).GetProperty("nextBillingAt").GetString()![..10]);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"api/admin/subscriptions/{id}/next-billing-date", new { date = "2001-01-01" })).StatusCode);

        // clean up
        await shopper.PostAsync($"api/subscriptions/{id}/cancel", null);
    }

    private static async Task<string> CreatePlan(HttpClient admin, string label, decimal price)
    {
        var name = $"{label} {Guid.NewGuid():N}"[..28];
        var response = await admin.PostAsJsonAsync("api/admin/plans", new { name, price, brand = "Azure" });
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
        return (await Json(response)).GetProperty("handle").GetString()!;
    }
}
