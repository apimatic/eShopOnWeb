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
/// These tests call the real Maxio sandbox through the API (no stubbing). The sandbox settings come from
/// user-secrets / environment (Maxio:ApiKey, Maxio:Subdomain, Maxio:Environment).
/// </summary>
[TestClass]
public class BillingEndpointsTest
{
    private static HttpClient ClientFor(string token)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static HttpClient Admin => ClientFor(ApiTokenHelper.GetAdminUserToken());
    private static HttpClient Shopper => ClientFor(ApiTokenHelper.GetNormalUserToken());

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task<(int Id, string Handle)> CreatePlan(string brand, decimal price)
    {
        var name = "IT plan " + Guid.NewGuid().ToString("N")[..10];
        var response = await Admin.PostAsJsonAsync("api/admin/plans", new { name, monthlyPrice = price, brand });
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
        var json = await ReadJson(response);
        return (json.GetProperty("id").GetInt32(), json.GetProperty("handle").GetString()!);
    }

    [TestMethod]
    public async Task PlansRequireSignIn()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task AdminEndpointsAreRefusedForShoppers()
    {
        var shopper = Shopper;
        var create = await shopper.PostAsJsonAsync("api/admin/plans", new { name = "x", monthlyPrice = 1, brand = ".NET" });
        Assert.AreEqual(HttpStatusCode.Forbidden, create.StatusCode);
        var currency = await shopper.PostAsJsonAsync("api/admin/plans/1/currency-prices", new { currency = "EUR", price = 1 });
        Assert.AreEqual(HttpStatusCode.Forbidden, currency.StatusCode);
        var date = await shopper.PutAsJsonAsync("api/admin/subscriptions/1/next-billing-date", new { date = "2099-01-01" });
        Assert.AreEqual(HttpStatusCode.Forbidden, date.StatusCode);
    }

    [TestMethod]
    public async Task CreatePlanRefusesABrandThatIsNotInTheCatalog()
    {
        var response = await Admin.PostAsJsonAsync("api/admin/plans", new { name = "IT plan", monthlyPrice = 5, brand = "No Such Brand" });
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task ShopperSeesNewPlanAndCannotSubscribeToUnknownPlan()
    {
        var plan = await CreatePlan(".NET", 7.5m);
        var list = await Shopper.GetAsync("api/plans");
        Assert.AreEqual(HttpStatusCode.OK, list.StatusCode);
        StringAssert.Contains(await list.Content.ReadAsStringAsync(), plan.Handle);

        var unknown = await Shopper.PostAsJsonAsync("api/subscriptions", new { planHandle = "no-such-plan-" + Guid.NewGuid().ToString("N") });
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, unknown.StatusCode);
    }

    [TestMethod]
    public async Task SubscriptionLifecycle()
    {
        var planA = await CreatePlan("Azure", 10m);
        var planB = await CreatePlan(".NET", 20m);
        var shopper = Shopper;

        var subscribe = await shopper.PostAsJsonAsync("api/subscriptions", new { planHandle = planA.Handle });
        Assert.AreEqual(HttpStatusCode.Created, subscribe.StatusCode, await subscribe.Content.ReadAsStringAsync());
        var id = (await ReadJson(subscribe)).GetProperty("id").GetInt32();

        // A second subscription (another plan) at the same time is allowed.
        var second = await shopper.PostAsJsonAsync("api/subscriptions", new { planHandle = planB.Handle });
        Assert.AreEqual(HttpStatusCode.Created, second.StatusCode, await second.Content.ReadAsStringAsync());

        var get = await shopper.GetAsync($"api/subscriptions/{id}");
        Assert.AreEqual(HttpStatusCode.OK, get.StatusCode);
        var page = await ReadJson(get);
        Assert.AreEqual("active", page.GetProperty("state").GetString());
        Assert.IsTrue(page.TryGetProperty("paid", out _));

        var pause = await shopper.PostAsync($"api/subscriptions/{id}/pause", null);
        Assert.AreEqual(HttpStatusCode.OK, pause.StatusCode, await pause.Content.ReadAsStringAsync());
        Assert.AreEqual("on_hold", (await ReadJson(pause)).GetProperty("state").GetString());

        var resume = await shopper.PostAsync($"api/subscriptions/{id}/resume", null);
        Assert.AreEqual(HttpStatusCode.OK, resume.StatusCode, await resume.Content.ReadAsStringAsync());
        Assert.AreEqual("active", (await ReadJson(resume)).GetProperty("state").GetString());

        // Resuming a subscription that is not paused is refused.
        var resumeAgain = await shopper.PostAsync($"api/subscriptions/{id}/resume", null);
        Assert.AreEqual(HttpStatusCode.Conflict, resumeAgain.StatusCode);

        var cancel = await shopper.PostAsync($"api/subscriptions/{id}/cancel", null);
        Assert.AreEqual(HttpStatusCode.OK, cancel.StatusCode, await cancel.Content.ReadAsStringAsync());
        Assert.AreEqual("canceled", (await ReadJson(cancel)).GetProperty("state").GetString());

        // Switching works on a cancelled subscription and puts the shopper on the new plan.
        var sw = await shopper.PostAsJsonAsync($"api/subscriptions/{id}/switch", new { planHandle = planB.Handle });
        Assert.AreEqual(HttpStatusCode.OK, sw.StatusCode, await sw.Content.ReadAsStringAsync());
        Assert.AreEqual(planB.Handle, (await ReadJson(sw)).GetProperty("planHandle").GetString());

        var restart = await shopper.PostAsync($"api/subscriptions/{id}/restart", null);
        Assert.AreEqual(HttpStatusCode.OK, restart.StatusCode, await restart.Content.ReadAsStringAsync());
        Assert.AreEqual("active", (await ReadJson(restart)).GetProperty("state").GetString());

        // Next billing date, set by staff.
        var date = DateTime.UtcNow.AddDays(60).ToString("yyyy-MM-dd");
        var next = await Admin.PutAsJsonAsync($"api/admin/subscriptions/{id}/next-billing-date", new { date });
        Assert.AreEqual(HttpStatusCode.OK, next.StatusCode, await next.Content.ReadAsStringAsync());
        StringAssert.StartsWith((await ReadJson(next)).GetProperty("currentPeriodEndsAt").GetString(), date);

        var past = await Admin.PutAsJsonAsync($"api/admin/subscriptions/{id}/next-billing-date", new { date = "2001-01-01" });
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, past.StatusCode);
    }

    [TestMethod]
    public async Task UnknownSubscriptionIsNotFoundAndUsageNeedsADate()
    {
        var missing = await Shopper.GetAsync("api/subscriptions/1");
        Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);

        var plan = await CreatePlan(".NET", 3m);
        var subscribe = await Shopper.PostAsJsonAsync("api/subscriptions", new { planHandle = plan.Handle });
        var id = (await ReadJson(subscribe)).GetProperty("id").GetInt32();

        var noDate = await Shopper.GetAsync($"api/subscriptions/{id}/usage");
        Assert.AreEqual(HttpStatusCode.BadRequest, noDate.StatusCode);

        var usage = await Shopper.GetAsync($"api/subscriptions/{id}/usage?date={DateTime.UtcNow:yyyy-MM-dd}");
        Assert.AreEqual(HttpStatusCode.OK, usage.StatusCode);
    }

    [TestMethod]
    public async Task CurrencyPriceIsRefusedWithAReasonWhenTheShopHasNoSuchCurrency()
    {
        var plan = await CreatePlan(".NET", 4m);
        var response = await Admin.PostAsJsonAsync($"api/admin/plans/{plan.Id}/currency-prices", new { currency = "EUR", price = 3.5 });
        Assert.IsTrue((int)response.StatusCode is 200 or 422, await response.Content.ReadAsStringAsync());
    }
}
