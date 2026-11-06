using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.BillingEndpoints;

/// <summary>
/// These run against the real Maxio sandbox: the credentials come from the "Maxio:" user-secrets of
/// the PublicApi project. Nothing is stubbed.
/// </summary>
[TestClass]
public class BillingEndpointsTests
{
    private static HttpClient ClientFor(string token)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [TestMethod]
    public async Task PlansRequireSignIn()
    {
        var response = await ProgramTest.NewClient.GetAsync("/api/plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task StaffFeaturesAreClosedToShoppers()
    {
        var client = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var response = await client.PostAsJsonAsync("/api/admin/plans", new { name = "x", price = 1, brand = "Azure" });
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task ShopperCanSubscribeSeeAndPauseAndResume()
    {
        var shopper = ClientFor(ApiTokenHelper.GetNormalUserToken());

        var plans = await shopper.GetAsync("/api/plans");
        Assert.AreEqual(HttpStatusCode.OK, plans.StatusCode);
        var list = Parse(await plans.Content.ReadAsStringAsync()).GetProperty("plans");
        Assert.IsTrue(list.GetArrayLength() > 0);
        var handle = list[0].GetProperty("handle").GetString();

        var created = await shopper.PostAsJsonAsync("/api/subscriptions", new { planHandle = handle });
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());
        var sub = Parse(await created.Content.ReadAsStringAsync()).GetProperty("subscription");
        var id = sub.GetProperty("id").GetInt32();

        var page = await shopper.GetAsync($"/api/subscriptions/{id}");
        Assert.AreEqual(HttpStatusCode.OK, page.StatusCode);
        Assert.IsTrue(Parse(await page.Content.ReadAsStringAsync()).GetProperty("subscription").TryGetProperty("paid", out _));

        var pause = await shopper.PostAsync($"/api/subscriptions/{id}/pause", null);
        Assert.AreEqual(HttpStatusCode.OK, pause.StatusCode, await pause.Content.ReadAsStringAsync());
        var resume = await shopper.PostAsync($"/api/subscriptions/{id}/resume", null);
        Assert.AreEqual(HttpStatusCode.OK, resume.StatusCode, await resume.Content.ReadAsStringAsync());

        var cancel = await shopper.PostAsync($"/api/subscriptions/{id}/cancel", null);
        Assert.AreEqual(HttpStatusCode.OK, cancel.StatusCode, await cancel.Content.ReadAsStringAsync());
        var again = await shopper.PostAsync($"/api/subscriptions/{id}/cancel", null);
        Assert.AreEqual(HttpStatusCode.Conflict, again.StatusCode);
    }

    [TestMethod]
    public async Task SomeoneElsesSubscriptionIsNotFound()
    {
        var shopper = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var created = await shopper.PostAsJsonAsync("/api/subscriptions", new { planHandle = "basic-plan" });
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());
        var id = Parse(await created.Content.ReadAsStringAsync()).GetProperty("subscription").GetProperty("id").GetInt32();

        var other = ClientFor(ApiTokenHelper.GetAdminUserToken());
        var response = await other.GetAsync($"/api/subscriptions/{id}");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task UsageNeedsAValidDate()
    {
        var shopper = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var response = await shopper.GetAsync("/api/subscriptions/1/usage?date=not-a-date");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task StaffPlanMustUseACatalogBrand()
    {
        var admin = ClientFor(ApiTokenHelper.GetAdminUserToken());
        var response = await admin.PostAsJsonAsync("/api/admin/plans", new { name = "Brandless", price = 5, brand = "No Such Brand" });
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
