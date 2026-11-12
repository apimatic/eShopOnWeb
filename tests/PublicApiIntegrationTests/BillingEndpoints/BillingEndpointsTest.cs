using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.BillingEndpoints;

/// <summary>
/// These tests talk to the real Maxio sandbox site named by the Maxio: settings (user-secrets);
/// nothing about Maxio is stubbed.
/// </summary>
[TestClass]
public class BillingEndpointsTest
{
    private static HttpClient Client(string? token)
    {
        var client = ProgramTest.NewClient;
        if (token != null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    [TestMethod]
    public async Task PlansRequireSignIn()
    {
        var response = await Client(null).GetAsync("api/plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task StaffEndpointsRefuseShoppers()
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("api/admin/plans", new { name = "x", monthlyPrice = 1, brand = "Azure" })).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync("api/admin/subscriptions/1/next-billing-date", new { date = "2030-01-01" })).StatusCode);
    }

    [TestMethod]
    public async Task UnknownBrandIsRefusedWithAReason()
    {
        var response = await Client(ApiTokenHelper.GetAdminUserToken()).PostAsJsonAsync("api/admin/plans", new { name = "Brandless", monthlyPrice = 3, brand = "No such brand" });
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        StringAssert.Contains((await Json(response)).GetProperty("Message").GetString(), "brand");
    }

    [TestMethod]
    public async Task ShopperLifecycleAgainstSandbox()
    {
        var admin = Client(ApiTokenHelper.GetAdminUserToken());
        var shopper = Client(ApiTokenHelper.GetNormalUserToken());

        var handle = "it-" + Guid.NewGuid().ToString("N")[..10];
        var created = await admin.PostAsJsonAsync("api/admin/plans", new { name = handle, monthlyPrice = 7.25m, brand = ".NET" });
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());

        var plans = await Json(await shopper.GetAsync("api/plans"));
        Assert.IsTrue(plans.GetProperty("plans").EnumerateArray().Any(p => p.GetProperty("handle").GetString() == handle));

        var unknown = await shopper.PostAsJsonAsync("api/subscriptions", new { planHandle = "no-such-plan-" + handle });
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, unknown.StatusCode);

        var sub = await shopper.PostAsJsonAsync("api/subscriptions", new { planHandle = handle });
        Assert.AreEqual(HttpStatusCode.Created, sub.StatusCode, await sub.Content.ReadAsStringAsync());
        var id = (await Json(sub)).GetProperty("id").GetInt32();

        Assert.AreEqual(HttpStatusCode.NotFound, (await admin.GetAsync($"api/subscriptions/{id}")).StatusCode, "another user must not see it");

        var page = await Json(await shopper.GetAsync($"api/subscriptions/{id}"));
        Assert.AreEqual("active", page.GetProperty("state").GetString());
        Assert.AreEqual(JsonValueKind.False, page.GetProperty("paid").ValueKind, "an open invoice is owed");

        Assert.AreEqual(HttpStatusCode.OK, (await shopper.PostAsync($"api/subscriptions/{id}/pause", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await shopper.PostAsync($"api/subscriptions/{id}/resume", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, (await shopper.PostAsync($"api/subscriptions/{id}/restart", null)).StatusCode);

        var usage = await shopper.GetAsync($"api/subscriptions/{id}/usage?date=2026-01-01");
        Assert.AreEqual(HttpStatusCode.OK, usage.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await shopper.GetAsync($"api/subscriptions/{id}/usage?date=nope")).StatusCode);

        var date = DateTime.UtcNow.AddDays(20).ToString("yyyy-MM-dd");
        Assert.AreEqual(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"api/admin/subscriptions/{id}/next-billing-date", new { date })).StatusCode);

        Assert.AreEqual(HttpStatusCode.OK, (await shopper.PostAsync($"api/subscriptions/{id}/cancel", null)).StatusCode);
        var plansAfter = await Json(await shopper.GetAsync("api/plans"));
        var other = plansAfter.GetProperty("plans").EnumerateArray().Select(p => p.GetProperty("handle").GetString()).First(h => h != handle);
        var switched = await Json(await shopper.PostAsJsonAsync($"api/subscriptions/{id}/switch", new { planHandle = other }));
        Assert.AreEqual(other, switched.GetProperty("planHandle").GetString());
        Assert.AreEqual(HttpStatusCode.OK, (await shopper.PostAsync($"api/subscriptions/{id}/restart", null)).StatusCode);
    }
}
