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
/// These tests call the real Maxio sandbox (credentials come from user-secrets: Maxio:ApiKey, Maxio:Subdomain,
/// Maxio:Environment). Nothing is stubbed.
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
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    [TestMethod]
    public async Task PlansRequireSignIn()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task StaffFeaturesAreForbiddenForShoppers()
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());
        var created = await client.PostAsJsonAsync("api/admin/plans", new { name = "x", monthlyPrice = 1, brandId = 1 });
        Assert.AreEqual(HttpStatusCode.Forbidden, created.StatusCode);
        var billed = await client.PutAsJsonAsync("api/admin/subscriptions/1/next-billing-date", new { date = "2099-01-01" });
        Assert.AreEqual(HttpStatusCode.Forbidden, billed.StatusCode);
    }

    [TestMethod]
    public async Task NewPlanMustBelongToACatalogBrand()
    {
        var client = Client(ApiTokenHelper.GetAdminUserToken());
        var response = await client.PostAsJsonAsync("api/admin/plans", new { name = "No Such Brand", monthlyPrice = 5, brandId = 99999 });
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task UsageNeedsAValidDate()
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());
        var response = await client.GetAsync("api/subscriptions/1/usage?date=yesterday");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task ShopperCanRunASubscriptionThroughItsLifecycle()
    {
        var admin = Client(ApiTokenHelper.GetAdminUserToken());
        var shopper = Client(ApiTokenHelper.GetNormalUserToken());

        // staff add two plans under a catalog brand
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var planA = await admin.PostAsJsonAsync("api/admin/plans", new { name = "Lifecycle A " + suffix, monthlyPrice = 10.50m, brandId = 1 });
        Assert.AreEqual(HttpStatusCode.Created, planA.StatusCode, await planA.Content.ReadAsStringAsync());
        var planB = await admin.PostAsJsonAsync("api/admin/plans", new { name = "Lifecycle B " + suffix, monthlyPrice = 20m, brandId = 1 });
        Assert.AreEqual(HttpStatusCode.Created, planB.StatusCode);
        var handleA = (await Json(planA)).GetProperty("handle").GetString();
        var handleB = (await Json(planB)).GetProperty("handle").GetString();

        // the shopper sees them
        var plans = await Json(await shopper.GetAsync("api/plans"));
        var handles = plans.EnumerateArray().Select(p => p.GetProperty("handle").GetString()).ToList();
        CollectionAssert.IsSubsetOf(new[] { handleA, handleB }, handles);

        // subscribe without card details, then read it back: money is owed, so paid is false
        var subscribed = await shopper.PostAsJsonAsync("api/subscriptions", new { planHandle = handleA });
        Assert.AreEqual(HttpStatusCode.Created, subscribed.StatusCode, await subscribed.Content.ReadAsStringAsync());
        var id = (await Json(subscribed)).GetProperty("id").GetInt32();
        var page = await Json(await shopper.GetAsync($"api/subscriptions/{id}"));
        Assert.IsFalse(page.GetProperty("paid").GetBoolean());

        // pause / resume
        Assert.AreEqual(HttpStatusCode.OK, (await shopper.PostAsync($"api/subscriptions/{id}/pause", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"api/subscriptions/{id}/pause", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await shopper.PostAsync($"api/subscriptions/{id}/resume", null)).StatusCode);

        // cancel / switch while cancelled / restart rules
        Assert.AreEqual(HttpStatusCode.OK, (await shopper.PostAsync($"api/subscriptions/{id}/cancel", null)).StatusCode);
        var switched = await shopper.PostAsJsonAsync($"api/subscriptions/{id}/switch", new { planHandle = handleB });
        Assert.AreEqual(HttpStatusCode.OK, switched.StatusCode, await switched.Content.ReadAsStringAsync());
        var after = await Json(switched);
        Assert.AreEqual(handleB, after.GetProperty("planHandle").GetString());
        Assert.AreEqual("active", after.GetProperty("state").GetString());
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"api/subscriptions/{id}/restart", null)).StatusCode);

        // usage for a day with nothing recorded is empty
        var usage = await shopper.GetAsync($"api/subscriptions/{id}/usage?date=2020-01-15");
        Assert.AreEqual(HttpStatusCode.OK, usage.StatusCode);

        // staff set the next billing date
        var date = DateTime.UtcNow.AddDays(20).ToString("yyyy-MM-dd");
        var billing = await admin.PutAsJsonAsync($"api/admin/subscriptions/{id}/next-billing-date", new { date });
        Assert.AreEqual(HttpStatusCode.OK, billing.StatusCode, await billing.Content.ReadAsStringAsync());

        // clean up: the subscription is someone else's for the admin account
        Assert.AreEqual(HttpStatusCode.NotFound, (await admin.GetAsync($"api/subscriptions/{id}")).StatusCode);
        await shopper.PostAsync($"api/subscriptions/{id}/cancel", null);
    }
}
