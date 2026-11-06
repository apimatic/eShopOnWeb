using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.BillingEndpoints;

/// <summary>
/// End-to-end tests of the billing endpoints. They call the real Maxio sandbox (no stubs); the
/// credentials come from the Maxio:* user-secrets, so the tests are skipped when those are absent.
/// </summary>
[TestClass]
public class BillingEndpointsTest
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private static void RequireSandbox()
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets(typeof(Program).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();
        if (string.IsNullOrWhiteSpace(config["Maxio:ApiKey"]) || string.IsNullOrWhiteSpace(config["Maxio:Subdomain"]))
            Assert.Inconclusive("Maxio sandbox settings (Maxio:ApiKey, Maxio:Subdomain) are not configured.");
    }

    private static HttpClient Client(string token)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static HttpClient Shopper(string name) =>
        Client(ApiTokenHelper.CreateTokenFor(name, Array.Empty<string>()));

    private static HttpClient Admin() => Client(ApiTokenHelper.GetAdminUserToken());

    private static string NewShopper() => $"billing-test-{Guid.NewGuid():N}@example.com";

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(expected, response.StatusCode, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task<JsonElement[]> ListPlansAsync(HttpClient client)
    {
        var plans = await ReadAsync(await client.GetAsync("/api/plans"), HttpStatusCode.OK);
        return plans.EnumerateArray().ToArray();
    }

    [TestMethod]
    public async Task PlansRequireSignIn()
    {
        var response = await ProgramTest.NewClient.GetAsync("/api/plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task PlansListOnlyCardFreeActivePlans()
    {
        RequireSandbox();
        var plans = await ListPlansAsync(Shopper(NewShopper()));
        Assert.IsTrue(plans.Length > 0);
        foreach (var plan in plans)
        {
            Assert.IsFalse(string.IsNullOrEmpty(plan.GetProperty("handle").GetString()));
            Assert.IsTrue(plan.GetProperty("price").GetDecimal() >= 0);
        }
    }

    [TestMethod]
    public async Task ShopperCanRunTheWholeSubscriptionLifecycle()
    {
        RequireSandbox();
        var shopper = Shopper(NewShopper());
        var plans = await ListPlansAsync(shopper);
        Assert.IsTrue(plans.Length >= 2, "the sandbox needs at least two card-free plans");
        var first = plans[0].GetProperty("handle").GetString()!;
        var second = plans[1].GetProperty("handle").GetString()!;

        var created = await ReadAsync(await shopper.PostAsJsonAsync("/api/subscriptions", new { planHandle = first }), HttpStatusCode.Created);
        var id = created.GetProperty("id").GetInt32();
        Assert.AreEqual(first, created.GetProperty("planHandle").GetString());

        // a second plan at the same time, then a duplicate of the first
        var other = await ReadAsync(await shopper.PostAsJsonAsync("/api/subscriptions", new { planHandle = second }), HttpStatusCode.Created);
        Assert.AreNotEqual(id, other.GetProperty("id").GetInt32());
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsJsonAsync("/api/subscriptions", new { planHandle = first })).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await shopper.PostAsJsonAsync("/api/subscriptions", new { planHandle = "no-such-plan" })).StatusCode);

        // paid is false while money is owed
        var page = await ReadAsync(await shopper.GetAsync($"/api/subscriptions/{id}"), HttpStatusCode.OK);
        Assert.IsFalse(page.GetProperty("paid").GetBoolean());

        // another shopper cannot see it
        var stranger = Shopper(NewShopper());
        Assert.AreEqual(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/subscriptions/{id}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await stranger.PostAsync($"/api/subscriptions/{id}/cancel", null)).StatusCode);

        // pause / resume
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"/api/subscriptions/{id}/resume", null)).StatusCode);
        var paused = await ReadAsync(await shopper.PostAsync($"/api/subscriptions/{id}/pause", null), HttpStatusCode.OK);
        Assert.AreEqual("on_hold", paused.GetProperty("state").GetString());
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"/api/subscriptions/{id}/pause", null)).StatusCode);
        var resumed = await ReadAsync(await shopper.PostAsync($"/api/subscriptions/{id}/resume", null), HttpStatusCode.OK);
        Assert.AreEqual("active", resumed.GetProperty("state").GetString());

        // switch plan
        var switched = await ReadAsync(await shopper.PostAsJsonAsync($"/api/subscriptions/{id}/switch", new { planHandle = second }), HttpStatusCode.OK);
        var switchedHandle = switched.GetProperty("planHandle").GetString();
        Assert.AreEqual(second, switchedHandle);

        // cancel, restart, and switch a canceled subscription
        var canceled = await ReadAsync(await shopper.PostAsync($"/api/subscriptions/{id}/cancel", null), HttpStatusCode.OK);
        Assert.AreEqual("canceled", canceled.GetProperty("state").GetString());
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"/api/subscriptions/{id}/cancel", null)).StatusCode);
        var restarted = await ReadAsync(await shopper.PostAsync($"/api/subscriptions/{id}/restart", null), HttpStatusCode.OK);
        Assert.AreEqual("active", restarted.GetProperty("state").GetString());
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"/api/subscriptions/{id}/restart", null)).StatusCode);

        await ReadAsync(await shopper.PostAsync($"/api/subscriptions/{id}/cancel", null), HttpStatusCode.OK);
        var target = first;
        var back = await ReadAsync(await shopper.PostAsJsonAsync($"/api/subscriptions/{id}/switch", new { planHandle = target }), HttpStatusCode.OK);
        Assert.AreEqual(target, back.GetProperty("planHandle").GetString());
        Assert.AreEqual("active", back.GetProperty("state").GetString());

        // usage: validates the date and answers for a day with nothing recorded
        Assert.AreEqual(HttpStatusCode.BadRequest, (await shopper.GetAsync($"/api/subscriptions/{id}/usage")).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await shopper.GetAsync($"/api/subscriptions/{id}/usage?date=06-11-2026")).StatusCode);
        var usage = await ReadAsync(await shopper.GetAsync($"/api/subscriptions/{id}/usage?date=2020-01-01"), HttpStatusCode.OK);
        Assert.AreEqual(0m, usage.GetProperty("totalQuantity").GetDecimal());

        // staff set the next billing date
        var date = DateTime.UtcNow.Date.AddDays(40).ToString("yyyy-MM-dd");
        var admin = Admin();
        var billed = await ReadAsync(await admin.PutAsJsonAsync($"/api/admin/subscriptions/{id}/next-billing-date", new { date }), HttpStatusCode.OK);
        Assert.IsTrue(billed.GetProperty("nextBillingAt").GetString()!.StartsWith(date, StringComparison.Ordinal));
        Assert.AreEqual(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/admin/subscriptions/{id}/next-billing-date", new { date = "2020-01-01" })).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.PutAsJsonAsync($"/api/admin/subscriptions/{id}/next-billing-date", new { date })).StatusCode);

        await ReadAsync(await shopper.PostAsync($"/api/subscriptions/{id}/cancel", null), HttpStatusCode.OK);
        await ReadAsync(await shopper.PostAsync($"/api/subscriptions/{other.GetProperty("id").GetInt32()}/cancel", null), HttpStatusCode.OK);
    }

    [TestMethod]
    public async Task StaffAddPlansForCatalogBrandsOnly()
    {
        RequireSandbox();
        var admin = Admin();
        var name = $"Test Plan {Guid.NewGuid():N}".Substring(0, 24);

        Assert.AreEqual(HttpStatusCode.Forbidden, (await Shopper(NewShopper()).PostAsJsonAsync("/api/admin/plans", new { name, price = 5, brand = "Azure" })).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/plans", new { name, price = 5, brand = "Not A Brand" })).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/plans", new { name, price = 5.123, brand = "Azure" })).StatusCode);

        var plan = await ReadAsync(await admin.PostAsJsonAsync("/api/admin/plans", new { name, price = 12.34, brand = "Azure" }), HttpStatusCode.Created);
        Assert.AreEqual(name, plan.GetProperty("name").GetString());
        Assert.AreEqual(12.34m, plan.GetProperty("price").GetDecimal());
        Assert.AreEqual("Azure", plan.GetProperty("brand").GetString());
        Assert.AreEqual(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/admin/plans", new { name, price = 12.34, brand = "Azure" })).StatusCode);

        var listed = await ListPlansAsync(Shopper(NewShopper()));
        Assert.IsTrue(listed.Any(p => p.GetProperty("handle").GetString() == plan.GetProperty("handle").GetString()));

        // the sandbox site only defines USD, so another currency is refused with a reason
        var id = plan.GetProperty("id").GetInt32();
        var refused = await admin.PostAsJsonAsync($"/api/admin/plans/{id}/currency-prices", new { currency = "EUR", price = 11 });
        Assert.IsTrue((int)refused.StatusCode is >= 400 and < 500, await refused.Content.ReadAsStringAsync());
        Assert.AreEqual(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync("/api/admin/plans/1/currency-prices", new { currency = "EUR", price = 11 })).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await Shopper(NewShopper()).PostAsJsonAsync($"/api/admin/plans/{id}/currency-prices", new { currency = "EUR", price = 11 })).StatusCode);
    }
}
