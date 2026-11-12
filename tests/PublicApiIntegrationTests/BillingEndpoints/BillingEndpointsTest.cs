using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.BillingEndpoints;

/// <summary>
/// Runs against the Maxio sandbox. The credentials are taken from the environment variables
/// MAXIO_API_KEY, MAXIO_SITE_SUBDOMAIN and MAXIO_ENVIRONMENT (never from a file); without them the
/// tests that need Maxio are reported inconclusive.
/// </summary>
[TestClass]
public class BillingEndpointsTest
{
    private static bool _maxioConfigured;

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        var key = Environment.GetEnvironmentVariable("MAXIO_API_KEY");
        var site = Environment.GetEnvironmentVariable("MAXIO_SITE_SUBDOMAIN");
        var env = Environment.GetEnvironmentVariable("MAXIO_ENVIRONMENT");
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(site) || string.IsNullOrWhiteSpace(env)) return;

        // Same keys the app reads from the "Maxio:" configuration section.
        Environment.SetEnvironmentVariable("Maxio__ApiKey", key);
        Environment.SetEnvironmentVariable("Maxio__Subdomain", site);
        Environment.SetEnvironmentVariable("Maxio__Environment", env);
        _maxioConfigured = true;
    }

    private static void RequireMaxio()
    {
        if (!_maxioConfigured) Assert.Inconclusive("MAXIO_* environment variables are not set.");
    }

    private static HttpClient ClientFor(string user, params string[] roles)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetTokenFor(user, roles));
        return client;
    }

    private static HttpClient Shopper() => ClientFor($"it-{Guid.NewGuid():N}@example.com");
    private static HttpClient Admin() => ClientFor("admin@microsoft.com", "Administrators");

    [TestMethod]
    public async Task PlansRequireSignIn()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task AdminEndpointsRefuseShoppers()
    {
        var shopper = Shopper();
        var created = await shopper.PostAsJsonAsync("api/admin/plans", new { name = "x", monthlyPrice = 1, brand = "Azure" });
        Assert.AreEqual(HttpStatusCode.Forbidden, created.StatusCode);
        var priced = await shopper.PostAsJsonAsync("api/admin/plans/1/currency-prices", new { currency = "EUR", price = 1 });
        Assert.AreEqual(HttpStatusCode.Forbidden, priced.StatusCode);
        var billed = await shopper.PutAsJsonAsync("api/admin/subscriptions/1/next-billing-date", new { date = "2030-01-01" });
        Assert.AreEqual(HttpStatusCode.Forbidden, billed.StatusCode);
    }

    [TestMethod]
    public async Task NewPlanMustUseACatalogBrand()
    {
        var response = await Admin().PostAsJsonAsync("api/admin/plans", new { name = "x", monthlyPrice = 1, brand = "Not A Brand" });
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task ShopperCanSubscribeManageAndSwitch()
    {
        RequireMaxio();

        // Staff add a plan (brand from the shop's catalog) ...
        var admin = Admin();
        var name = $"IT Plan {Guid.NewGuid():N}"[..20];
        var createdPlan = await admin.PostAsJsonAsync("api/admin/plans", new { name, monthlyPrice = 7.5m, brand = "Azure" });
        Assert.AreEqual(HttpStatusCode.Created, createdPlan.StatusCode, await createdPlan.Content.ReadAsStringAsync());
        var plan = (await createdPlan.Content.ReadFromJsonAsync<JsonElement>());
        var planHandle = plan.GetProperty("handle").GetString()!;
        Assert.AreEqual("Azure", plan.GetProperty("brand").GetString());
        Assert.AreEqual(7.5m, plan.GetProperty("monthlyPrice").GetDecimal());

        var shopper = Shopper();

        // ... which the shopper sees in the plan list
        var plans = await shopper.GetFromJsonAsync<JsonElement>("api/plans");
        Assert.IsTrue(plans.EnumerateArray().Any(p => p.GetProperty("handle").GetString() == planHandle));

        // an unknown plan is refused
        var unknown = await shopper.PostAsJsonAsync("api/subscriptions", new { planHandle = "no-such-plan-handle" });
        Assert.AreEqual(HttpStatusCode.NotFound, unknown.StatusCode);

        // subscribe without card details; a second subscription to the same plan is refused
        var subscribed = await shopper.PostAsJsonAsync("api/subscriptions", new { planHandle });
        Assert.AreEqual(HttpStatusCode.Created, subscribed.StatusCode, await subscribed.Content.ReadAsStringAsync());
        var sub = await subscribed.Content.ReadFromJsonAsync<JsonElement>();
        var id = sub.GetProperty("id").GetInt32();
        Assert.AreEqual("active", sub.GetProperty("state").GetString());
        Assert.IsFalse(sub.GetProperty("paid").GetBoolean(), "an unpaid invoice is outstanding");
        Assert.AreEqual((await shopper.PostAsJsonAsync("api/subscriptions", new { planHandle })).StatusCode, HttpStatusCode.Conflict);

        // someone else cannot see it
        var stranger = await Shopper().GetAsync($"api/subscriptions/{id}");
        Assert.AreEqual(HttpStatusCode.NotFound, stranger.StatusCode);

        // pause / resume
        Assert.AreEqual("on_hold", await StateAfter(shopper, $"api/subscriptions/{id}/pause"));
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"api/subscriptions/{id}/pause", null)).StatusCode);
        Assert.AreEqual("active", await StateAfter(shopper, $"api/subscriptions/{id}/resume"));

        // cancel / restart
        Assert.AreEqual("canceled", await StateAfter(shopper, $"api/subscriptions/{id}/cancel"));
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"api/subscriptions/{id}/cancel", null)).StatusCode);

        // switching a canceled subscription puts the shopper on the new plan
        var switched = await shopper.PostAsJsonAsync($"api/subscriptions/{id}/switch", new { planHandle = "basic-plan" });
        Assert.AreEqual(HttpStatusCode.OK, switched.StatusCode, await switched.Content.ReadAsStringAsync());
        var after = await switched.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual("basic-plan", after.GetProperty("planHandle").GetString());
        Assert.AreEqual("active", after.GetProperty("state").GetString());

        // staff set the next billing date; the subscription reflects it
        var date = DateTime.UtcNow.AddDays(45).ToString("yyyy-MM-dd");
        var billed = await admin.PutAsJsonAsync($"api/admin/subscriptions/{id}/next-billing-date", new { date });
        Assert.AreEqual(HttpStatusCode.OK, billed.StatusCode, await billed.Content.ReadAsStringAsync());
        var read = await shopper.GetFromJsonAsync<JsonElement>($"api/subscriptions/{id}");
        Assert.IsTrue(read.GetProperty("currentPeriodEndsAt").GetString()!.StartsWith(date));

        // metered usage for a day: valid date answers, malformed date is refused
        var usage = await shopper.GetAsync($"api/subscriptions/{id}/usage?date={date}");
        Assert.AreEqual(HttpStatusCode.OK, usage.StatusCode, await usage.Content.ReadAsStringAsync());
        Assert.AreEqual(HttpStatusCode.BadRequest, (await shopper.GetAsync($"api/subscriptions/{id}/usage?date=nope")).StatusCode);

        // clean up so the sandbox keeps no live test subscriptions
        Assert.AreEqual("canceled", await StateAfter(shopper, $"api/subscriptions/{id}/cancel"));
    }

    [TestMethod]
    public async Task CurrencyPriceIsRefusedWithAReasonWhenMaxioRefuses()
    {
        RequireMaxio();
        var admin = Admin();
        var plans = await ClientFor("it-currency@example.com").GetFromJsonAsync<JsonElement>("api/plans");
        var planId = plans.EnumerateArray().First().GetProperty("id").GetInt32();

        // a currency the sandbox site has not defined
        var response = await admin.PostAsJsonAsync($"api/admin/plans/{planId}/currency-prices", new { currency = "XTS", price = 10 });
        Assert.IsTrue((int)response.StatusCode is >= 400 and < 500, response.StatusCode.ToString());
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "Message");
    }

    private static async Task<string?> StateAfter(HttpClient client, string url)
    {
        var response = await client.PostAsync(url, null);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("state").GetString();
    }
}
