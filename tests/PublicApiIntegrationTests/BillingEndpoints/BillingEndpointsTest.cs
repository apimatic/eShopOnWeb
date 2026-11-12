using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.BillingEndpoints;

/// <summary>
/// These tests call the real Maxio sandbox (nothing is stubbed): the Maxio settings come from
/// user-secrets (Maxio:ApiKey, Maxio:Subdomain, Maxio:Environment).
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

    private static HttpClient Shopper => Client(ApiTokenHelper.GetNormalUserToken());
    private static HttpClient Admin => Client(ApiTokenHelper.GetAdminUserToken());

    private static StringContent Json(object body) =>
        new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    [TestMethod]
    public async Task PlansRequireSignIn()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ShopperCannotUseStaffEndpoints()
    {
        var plan = await Shopper.PostAsync("api/admin/plans", Json(new { name = "x", monthlyPrice = 1, brand = "Azure" }));
        Assert.AreEqual(HttpStatusCode.Forbidden, plan.StatusCode);

        var date = await Shopper.PutAsync("api/admin/subscriptions/1/next-billing-date", Json(new { date = "2030-01-01" }));
        Assert.AreEqual(HttpStatusCode.Forbidden, date.StatusCode);
    }

    [TestMethod]
    public async Task NewPlanMustUseACatalogBrand()
    {
        var response = await Admin.PostAsync("api/admin/plans", Json(new { name = "x", monthlyPrice = 1, brand = "No Such Brand" }));
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeRejectsAPlanTheShopIsNotOffering()
    {
        var response = await Shopper.PostAsync("api/subscriptions", Json(new { planHandle = "no-such-plan-handle" }));
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task UsageNeedsARealDate()
    {
        var response = await Shopper.GetAsync("api/subscriptions/1/usage?date=2026-02-30");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task ShopperSubscriptionLifecycleAgainstTheSandbox()
    {
        var shopper = Shopper;

        var plans = await ReadAsync(await shopper.GetAsync("api/plans"));
        var handles = plans.EnumerateArray().Select(p => p.GetProperty("handle").GetString()!).ToList();
        Assert.IsTrue(handles.Count >= 2, "the sandbox needs at least two plans");

        var created = await shopper.PostAsync("api/subscriptions", Json(new { planHandle = handles[0] }));
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());
        var sub = await ReadAsync(created);
        var id = sub.GetProperty("id").GetInt32();
        var url = $"api/subscriptions/{id}";

        // Someone else's subscription looks like a missing one.
        var other = await Admin.GetAsync(url);
        Assert.AreEqual(HttpStatusCode.NotFound, other.StatusCode);

        var page = await ReadAsync(await shopper.GetAsync(url));
        Assert.IsTrue(page.GetProperty("paid").ValueKind is JsonValueKind.True or JsonValueKind.False);

        var pause = await shopper.PostAsync(url + "/pause", null);
        Assert.AreEqual(HttpStatusCode.OK, pause.StatusCode, await pause.Content.ReadAsStringAsync());
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync(url + "/pause", null)).StatusCode);

        var resume = await shopper.PostAsync(url + "/resume", null);
        Assert.AreEqual(HttpStatusCode.OK, resume.StatusCode, await resume.Content.ReadAsStringAsync());

        var cancel = await shopper.PostAsync(url + "/cancel", null);
        Assert.AreEqual(HttpStatusCode.OK, cancel.StatusCode, await cancel.Content.ReadAsStringAsync());

        // A canceled subscription can still be switched, and ends up on the new plan.
        var switched = await shopper.PostAsync(url + "/switch", Json(new { planHandle = handles[1] }));
        Assert.AreEqual(HttpStatusCode.OK, switched.StatusCode, await switched.Content.ReadAsStringAsync());
        Assert.AreEqual(handles[1], (await ReadAsync(switched)).GetProperty("planHandle").GetString());

        var restart = await shopper.PostAsync(url + "/restart", null);
        Assert.AreEqual(HttpStatusCode.OK, restart.StatusCode, await restart.Content.ReadAsStringAsync());

        var usage = await shopper.GetAsync(url + "/usage?date=2026-11-13");
        Assert.AreEqual(HttpStatusCode.OK, usage.StatusCode, await usage.Content.ReadAsStringAsync());

        var billing = await Admin.PutAsync($"api/admin/subscriptions/{id}/next-billing-date", Json(new { date = "2031-03-15" }));
        Assert.AreEqual(HttpStatusCode.OK, billing.StatusCode, await billing.Content.ReadAsStringAsync());

        await shopper.PostAsync(url + "/cancel", null);
    }
}
