using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.BillingEndpoints;

/// <summary>
/// Exercises the billing endpoints against the Maxio sandbox. The credentials come from the
/// Maxio:* configuration (user-secrets); without them the live tests are reported as inconclusive.
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

    private static HttpClient Shopper() => Client(ApiTokenHelper.GetNormalUserToken());
    private static HttpClient Admin() => Client(ApiTokenHelper.GetAdminUserToken());

    private static void RequireMaxio()
    {
        var config = ProgramTest.NewClientServices.GetRequiredService<IConfiguration>();
        if (string.IsNullOrWhiteSpace(config["Maxio:ApiKey"]) || string.IsNullOrWhiteSpace(config["Maxio:Subdomain"]))
            Assert.Inconclusive("Maxio sandbox settings are not configured (user-secrets).");
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<string> FirstFreeShopperPlanHandle(HttpClient shopper)
    {
        var plans = await Json(await shopper.GetAsync("api/plans"));
        foreach (var p in plans.GetProperty("plans").EnumerateArray())
        {
            if (p.GetProperty("monthlyPrice").GetDecimal() == 0) return p.GetProperty("handle").GetString()!;
        }
        Assert.Inconclusive("The sandbox has no free plan to subscribe to.");
        return string.Empty;
    }

    // ---- authentication / authorization (no Maxio call is reached)

    [TestMethod]
    public async Task PlansRequireAToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscriptionsRequireAToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/subscriptions/1");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task StaffEndpointsRejectShoppers()
    {
        var shopper = Shopper();
        Assert.AreEqual(HttpStatusCode.Forbidden,
            (await shopper.PostAsJsonAsync("api/admin/plans", new { name = "x", monthlyPrice = 1, brand = "Azure" })).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden,
            (await shopper.PostAsJsonAsync("api/admin/plans/1/currency-prices", new { currency = "EUR", price = 1 })).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden,
            (await shopper.PutAsJsonAsync("api/admin/subscriptions/1/next-billing-date", new { date = "2099-01-01" })).StatusCode);
    }

    // ---- live, against the sandbox

    [TestMethod]
    public async Task ListsPlans()
    {
        RequireMaxio();
        var response = await Shopper().GetAsync("api/plans");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var plans = (await Json(response)).GetProperty("plans");
        Assert.IsTrue(plans.GetArrayLength() > 0);
        foreach (var p in plans.EnumerateArray())
        {
            Assert.IsFalse(string.IsNullOrEmpty(p.GetProperty("handle").GetString()));
        }
    }

    [TestMethod]
    public async Task SubscribeToUnknownPlanIsNotFound()
    {
        RequireMaxio();
        var response = await Shopper().PostAsJsonAsync("api/subscriptions", new { planHandle = "no-such-plan-" + Guid.NewGuid().ToString("N") });
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeWithoutPlanIsABadRequest()
    {
        RequireMaxio();
        var response = await Shopper().PostAsJsonAsync("api/subscriptions", new { });
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscriptionLifecycle()
    {
        RequireMaxio();
        var shopper = Shopper();
        var handle = await FirstFreeShopperPlanHandle(shopper);

        var created = await shopper.PostAsJsonAsync("api/subscriptions", new { planHandle = handle });
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        var sub = await Json(created);
        var id = sub.GetProperty("id").GetInt32();
        Assert.IsTrue(sub.GetProperty("paid").GetBoolean(), "a free plan owes nothing");

        var read = await Json(await shopper.GetAsync($"api/subscriptions/{id}"));
        Assert.AreEqual("active", read.GetProperty("state").GetString());

        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"api/subscriptions/{id}/resume", null)).StatusCode);

        var paused = await shopper.PostAsync($"api/subscriptions/{id}/pause", null);
        Assert.AreEqual(HttpStatusCode.OK, paused.StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"api/subscriptions/{id}/pause", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await shopper.PostAsync($"api/subscriptions/{id}/resume", null)).StatusCode);

        var cancelled = await Json(await shopper.PostAsync($"api/subscriptions/{id}/cancel", null));
        Assert.AreEqual("canceled", cancelled.GetProperty("state").GetString());
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"api/subscriptions/{id}/cancel", null)).StatusCode);

        var restarted = await Json(await shopper.PostAsync($"api/subscriptions/{id}/restart", null));
        Assert.AreEqual("active", restarted.GetProperty("state").GetString());
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"api/subscriptions/{id}/restart", null)).StatusCode);

        // Usage for a day with nothing recorded is an empty (not failing) answer; bad dates are rejected.
        var usage = await shopper.GetAsync($"api/subscriptions/{id}/usage?date=2000-01-01");
        Assert.AreEqual(HttpStatusCode.OK, usage.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await shopper.GetAsync($"api/subscriptions/{id}/usage?date=yesterday")).StatusCode);

        // Another caller cannot see or touch it.
        var other = Admin();
        Assert.AreEqual(HttpStatusCode.NotFound, (await other.GetAsync($"api/subscriptions/{id}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await other.PostAsync($"api/subscriptions/{id}/cancel", null)).StatusCode);

        await shopper.PostAsync($"api/subscriptions/{id}/cancel", null);
    }

    [TestMethod]
    public async Task SwitchingACancelledSubscriptionPutsItOnTheNewPlan()
    {
        RequireMaxio();
        var shopper = Shopper();
        var plans = (await Json(await shopper.GetAsync("api/plans"))).GetProperty("plans");
        if (plans.GetArrayLength() < 2) Assert.Inconclusive("Need at least two plans.");
        var first = plans[0].GetProperty("handle").GetString();
        var second = plans[1].GetProperty("handle").GetString();

        var id = (await Json(await shopper.PostAsJsonAsync("api/subscriptions", new { planHandle = first }))).GetProperty("id").GetInt32();
        await shopper.PostAsync($"api/subscriptions/{id}/cancel", null);

        var switched = await shopper.PostAsJsonAsync($"api/subscriptions/{id}/switch", new { planHandle = second });
        Assert.AreEqual(HttpStatusCode.OK, switched.StatusCode);
        var body = await Json(switched);
        Assert.AreEqual(second, body.GetProperty("plan").GetProperty("handle").GetString());
        Assert.AreEqual(HttpStatusCode.Conflict,
            (await shopper.PostAsJsonAsync($"api/subscriptions/{id}/switch", new { planHandle = second })).StatusCode);

        await shopper.PostAsync($"api/subscriptions/{id}/cancel", null);
    }

    [TestMethod]
    public async Task StaffCanAddAPlanAndSetANextBillingDate()
    {
        RequireMaxio();
        var admin = Admin();
        var name = "it-" + Guid.NewGuid().ToString("N")[..8];

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity,
            (await admin.PostAsJsonAsync("api/admin/plans", new { name, monthlyPrice = 5, brand = "Not A Brand" })).StatusCode);

        var created = await admin.PostAsJsonAsync("api/admin/plans", new { name, monthlyPrice = 5.5, brand = "Azure" });
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        var plan = await Json(created);
        Assert.AreEqual("Azure", plan.GetProperty("brand").GetString());
        Assert.AreEqual(5.5m, plan.GetProperty("monthlyPrice").GetDecimal());
        Assert.AreEqual(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync("api/admin/plans", new { name, monthlyPrice = 5.5, brand = "Azure" })).StatusCode);

        // The sandbox site has no additional currencies, so a currency price is refused with a reason.
        var currency = await admin.PostAsJsonAsync($"api/admin/plans/{plan.GetProperty("id").GetInt32()}/currency-prices", new { currency = "EUR", price = 5 });
        Assert.IsTrue((int)currency.StatusCode is >= 400 and < 500, "expected a 4xx for a currency the site does not have");

        var shopper = Shopper();
        var id = (await Json(await shopper.PostAsJsonAsync("api/subscriptions", new { planHandle = plan.GetProperty("handle").GetString() }))).GetProperty("id").GetInt32();
        var date = DateTime.UtcNow.AddDays(20).ToString("yyyy-MM-dd");
        var set = await admin.PutAsJsonAsync($"api/admin/subscriptions/{id}/next-billing-date", new { date });
        Assert.AreEqual(HttpStatusCode.OK, set.StatusCode);
        Assert.IsTrue((await Json(set)).GetProperty("nextBillingAt").GetString()!.StartsWith(date));
        Assert.AreEqual(HttpStatusCode.BadRequest,
            (await admin.PutAsJsonAsync($"api/admin/subscriptions/{id}/next-billing-date", new { date = "2001-01-01" })).StatusCode);

        var read = await Json(await shopper.GetAsync($"api/subscriptions/{id}"));
        Assert.IsFalse(read.GetProperty("paid").GetBoolean(), "a priced plan is invoiced and unpaid");
        await shopper.PostAsync($"api/subscriptions/{id}/cancel", null);
    }
}
