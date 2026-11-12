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
/// These tests talk to the real Maxio sandbox (credentials from the Maxio: configuration section,
/// i.e. .NET user-secrets). They are inconclusive when the sandbox is not configured.
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

    private static async Task<JsonElement> Json(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<JsonElement[]> PlansOrInconclusive(HttpClient client)
    {
        var response = await client.GetAsync("api/plans");
        if (response.StatusCode == HttpStatusCode.ServiceUnavailable) Assert.Inconclusive("Maxio sandbox is not configured.");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return (await Json(response)).GetProperty("plans").EnumerateArray().ToArray();
    }

    [TestMethod]
    public async Task BillingEndpointsRequireAToken()
    {
        var client = ProgramTest.NewClient;
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/plans")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/subscriptions/1")).StatusCode);
    }

    [TestMethod]
    public async Task StaffEndpointsRejectShoppers()
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());
        var response = await client.PostAsJsonAsync("api/admin/plans", new { name = "x", monthlyPrice = 1, brand = "Azure" });
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        response = await client.PutAsJsonAsync("api/admin/subscriptions/1/next-billing-date", new { date = "2030-01-01" });
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task ShopperCanRunASubscriptionLifecycle()
    {
        var shopper = Client(ApiTokenHelper.GetNormalUserToken());
        var plans = await PlansOrInconclusive(shopper);
        Assert.IsTrue(plans.Length >= 2, "The sandbox needs at least two plans.");
        var first = plans[0].GetProperty("handle").GetString()!;
        var second = plans.First(p => p.GetProperty("handle").GetString() != first).GetProperty("handle").GetString()!;

        var created = await shopper.PostAsJsonAsync("api/subscriptions", new { planHandle = first });
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        var id = (await Json(created)).GetProperty("subscriptionId").GetInt32();

        var page = await Json(await shopper.GetAsync($"api/subscriptions/{id}"));
        Assert.AreEqual(first, page.GetProperty("planHandle").GetString());
        Assert.AreEqual(page.GetProperty("balanceDue").GetDecimal() == 0, page.GetProperty("paid").GetBoolean());

        Assert.AreEqual("on_hold", (await Json(await shopper.PostAsync($"api/subscriptions/{id}/pause", null))).GetProperty("state").GetString());
        Assert.AreEqual("active", (await Json(await shopper.PostAsync($"api/subscriptions/{id}/resume", null))).GetProperty("state").GetString());
        Assert.AreEqual("canceled", (await Json(await shopper.PostAsync($"api/subscriptions/{id}/cancel", null))).GetProperty("state").GetString());

        // A cancelled subscription can still be switched, and switching puts the shopper on the new plan.
        var switched = await shopper.PostAsJsonAsync($"api/subscriptions/{id}/switch", new { planHandle = second });
        Assert.AreEqual(HttpStatusCode.OK, switched.StatusCode);
        Assert.AreEqual(second, (await Json(switched)).GetProperty("planHandle").GetString());

        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"api/subscriptions/{id}/restart", null)).StatusCode);
        await shopper.PostAsync($"api/subscriptions/{id}/cancel", null);
        Assert.AreEqual("active", (await Json(await shopper.PostAsync($"api/subscriptions/{id}/restart", null))).GetProperty("state").GetString());

        var usage = await shopper.GetAsync($"api/subscriptions/{id}/usage?date={DateTime.UtcNow:yyyy-MM-dd}");
        Assert.AreEqual(HttpStatusCode.OK, usage.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await shopper.GetAsync($"api/subscriptions/{id}/usage?date=nope")).StatusCode);

        // Someone else's subscription is not found.
        var staff = Client(ApiTokenHelper.GetAdminUserToken());
        Assert.AreEqual(HttpStatusCode.NotFound, (await staff.GetAsync($"api/subscriptions/{id}")).StatusCode);

        // Staff move the billing date.
        var date = DateTime.UtcNow.AddDays(20).ToString("yyyy-MM-dd");
        var moved = await staff.PutAsJsonAsync($"api/admin/subscriptions/{id}/next-billing-date", new { date });
        Assert.AreEqual(HttpStatusCode.OK, moved.StatusCode);
        StringAssert.StartsWith((await Json(moved)).GetProperty("nextBillingDate").GetString(), date);
        Assert.AreEqual(HttpStatusCode.BadRequest,
            (await staff.PutAsJsonAsync($"api/admin/subscriptions/{id}/next-billing-date", new { date = "2001-01-01" })).StatusCode);

        await shopper.PostAsync($"api/subscriptions/{id}/cancel", null);
    }

    [TestMethod]
    public async Task SubscribingToAPlanThatIsNotOnOfferIsRefused()
    {
        var shopper = Client(ApiTokenHelper.GetNormalUserToken());
        await PlansOrInconclusive(shopper);
        var response = await shopper.PostAsJsonAsync("api/subscriptions", new { planHandle = "no-such-plan-" + Guid.NewGuid().ToString("N") });
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task StaffCreatePlansOnlyForCatalogBrands()
    {
        var staff = Client(ApiTokenHelper.GetAdminUserToken());
        await PlansOrInconclusive(staff);
        var name = "Test plan " + Guid.NewGuid().ToString("N")[..8];

        var unknown = await staff.PostAsJsonAsync("api/admin/plans", new { name, monthlyPrice = 5, brand = "No Such Brand" });
        Assert.AreEqual(HttpStatusCode.BadRequest, unknown.StatusCode);

        var created = await staff.PostAsJsonAsync("api/admin/plans", new { name, monthlyPrice = 5.25, brand = "Azure" });
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        var plan = await Json(created);
        Assert.AreEqual(5.25m, plan.GetProperty("monthlyPrice").GetDecimal());
        Assert.AreEqual("Azure", plan.GetProperty("brand").GetString());

        var listed = await PlansOrInconclusive(staff);
        Assert.IsTrue(listed.Any(p => p.GetProperty("id").GetInt32() == plan.GetProperty("id").GetInt32()));

        var bad = await staff.PostAsJsonAsync($"api/admin/plans/{plan.GetProperty("id").GetInt32()}/currency-prices", new { currency = "??", price = 5 });
        Assert.AreEqual(HttpStatusCode.BadRequest, bad.StatusCode);
    }
}
