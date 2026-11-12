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
/// Runs against the real Maxio sandbox; the credentials come from the Maxio: configuration section (user-secrets).
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

    [TestMethod]
    public async Task ReturnsUnauthorizedGivenNoToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task AdminEndpointsReturnForbiddenGivenNormalUserToken()
    {
        var client = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var created = await client.PostAsJsonAsync("api/admin/plans", new { name = "x", monthlyPrice = 1, brandId = 1 });
        var priced = await client.PostAsJsonAsync("api/admin/plans/1/currency-prices", new { currency = "EUR", price = 1 });
        var billed = await client.PutAsJsonAsync("api/admin/subscriptions/1/next-billing-date", new { date = "2999-01-01" });

        Assert.AreEqual(HttpStatusCode.Forbidden, created.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, priced.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, billed.StatusCode);
    }

    [TestMethod]
    public async Task ReturnsBadRequestGivenBrandOutsideTheCatalog()
    {
        var client = ClientFor(ApiTokenHelper.GetAdminUserToken());
        var response = await client.PostAsJsonAsync("api/admin/plans", new { name = "Ghost", monthlyPrice = 1, brandId = 9999 });
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task ReturnsNotFoundGivenUnknownPlanHandle()
    {
        var client = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var response = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "no-such-plan" });
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task ShopperCanRunASubscriptionLifecycle()
    {
        var admin = ClientFor(ApiTokenHelper.GetAdminUserToken());
        var shopper = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var basic = await CreatePlan(admin, $"Basic {suffix}", 5m);
        var pro = await CreatePlan(admin, $"Pro {suffix}", 15m);

        var plans = await shopper.GetFromJsonAsync<JsonElement>("api/plans");
        StringAssert.Contains(plans.GetRawText(), basic);
        StringAssert.Contains(plans.GetRawText(), pro);

        var created = await shopper.PostAsJsonAsync("api/subscriptions", new { planHandle = basic });
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        var sub = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = sub.GetProperty("id").GetInt32();
        Assert.IsFalse(sub.GetProperty("paid").GetBoolean(), "an unpaid invoice is outstanding");

        // another user's subscription is invisible
        var other = await admin.GetAsync($"api/subscriptions/{id}");
        Assert.AreEqual(HttpStatusCode.NotFound, other.StatusCode);

        Assert.AreEqual(HttpStatusCode.OK, (await shopper.PostAsync($"api/subscriptions/{id}/pause", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"api/subscriptions/{id}/pause", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await shopper.PostAsync($"api/subscriptions/{id}/resume", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await shopper.PostAsync($"api/subscriptions/{id}/cancel", null)).StatusCode);

        var switched = await shopper.PostAsJsonAsync($"api/subscriptions/{id}/switch", new { planHandle = pro });
        Assert.AreEqual(HttpStatusCode.OK, switched.StatusCode);
        var after = await switched.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(pro, after.GetProperty("planHandle").GetString());

        Assert.AreEqual(HttpStatusCode.OK, (await shopper.PostAsync($"api/subscriptions/{id}/cancel", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await shopper.PostAsync($"api/subscriptions/{id}/restart", null)).StatusCode);

        var date = DateTime.UtcNow.AddDays(20).ToString("yyyy-MM-dd");
        var billed = await admin.PutAsJsonAsync($"api/admin/subscriptions/{id}/next-billing-date", new { date });
        Assert.AreEqual(HttpStatusCode.OK, billed.StatusCode);
        var billedBody = await billed.Content.ReadFromJsonAsync<JsonElement>();
        StringAssert.StartsWith(billedBody.GetProperty("nextBillingAt").GetString(), date);

        var usage = await shopper.GetAsync($"api/subscriptions/{id}/usage?date={DateTime.UtcNow:yyyy-MM-dd}");
        Assert.AreEqual(HttpStatusCode.OK, usage.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await shopper.GetAsync($"api/subscriptions/{id}/usage?date=nope")).StatusCode);
    }

    private static async Task<string> CreatePlan(HttpClient admin, string name, decimal price)
    {
        var response = await admin.PostAsJsonAsync("api/admin/plans", new { name, monthlyPrice = price, brandId = 2 });
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var plan = await response.Content.ReadFromJsonAsync<JsonElement>();
        return plan.GetProperty("handle").GetString()!;
    }
}
