using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// These tests call the real Maxio sandbox (credentials come from the Maxio: configuration section / user-secrets).
/// </summary>
[TestClass]
public class SubscriptionEndpointsTest
{
    private static HttpClient ClientFor(string token)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    [TestMethod]
    public async Task PlansRequireSignIn()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task AdminEndpointsAreForbiddenToShoppers()
    {
        var client = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var response = await client.PostAsJsonAsync("api/admin/plans", new { name = "x", monthlyPrice = 1, brand = "Azure" });
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task ShopperCanRunASubscriptionLifecycle()
    {
        var user = $"it-{Guid.NewGuid():N}@example.com";
        var client = ClientFor(ApiTokenHelper.CreateToken(user, new string[] { }));

        var plans = await JsonOf(await client.GetAsync("api/plans"));
        Assert.IsTrue(plans.GetProperty("plans").GetArrayLength() > 0);

        var created = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "br-probe-free" });
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        var id = (await JsonOf(created)).GetProperty("id").GetInt64();

        var page = await JsonOf(await client.GetAsync($"api/subscriptions/{id}"));
        Assert.IsTrue(page.GetProperty("paid").GetBoolean());

        // someone else's subscription does not exist for this shopper
        var other = ClientFor(ApiTokenHelper.CreateToken($"it-{Guid.NewGuid():N}@example.com", new string[] { }));
        Assert.AreEqual(HttpStatusCode.NotFound, (await other.GetAsync($"api/subscriptions/{id}")).StatusCode);

        Assert.AreEqual(HttpStatusCode.OK, (await client.PostAsync($"api/subscriptions/{id}/pause", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, (await client.PostAsync($"api/subscriptions/{id}/pause", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await client.PostAsync($"api/subscriptions/{id}/resume", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await client.PostAsync($"api/subscriptions/{id}/cancel", null)).StatusCode);

        var switched = await client.PostAsJsonAsync($"api/subscriptions/{id}/switch", new { planHandle = "br-probe-community" });
        Assert.AreEqual(HttpStatusCode.OK, switched.StatusCode);
        var body = await JsonOf(switched);
        Assert.AreEqual("br-probe-community", body.GetProperty("planHandle").GetString());
        Assert.AreEqual("active", body.GetProperty("state").GetString());

        var usage = await client.GetAsync($"api/subscriptions/{id}/usage?date={DateTime.UtcNow:yyyy-MM-dd}");
        Assert.AreEqual(HttpStatusCode.OK, usage.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await client.GetAsync($"api/subscriptions/{id}/usage?date=nope")).StatusCode);

        var admin = ClientFor(ApiTokenHelper.GetAdminUserToken());
        var date = DateTime.UtcNow.AddDays(20).ToString("yyyy-MM-dd");
        var set = await admin.PutAsJsonAsync($"api/admin/subscriptions/{id}/next-billing-date", new { date });
        Assert.AreEqual(HttpStatusCode.OK, set.StatusCode);
        Assert.IsTrue((await JsonOf(set)).GetProperty("nextBillingAt").GetString()!.StartsWith(date));

        await client.PostAsync($"api/subscriptions/{id}/cancel", null);
    }

    [TestMethod]
    public async Task StaffCannotAddAPlanForAnUnknownBrand()
    {
        var admin = ClientFor(ApiTokenHelper.GetAdminUserToken());
        var response = await admin.PostAsJsonAsync("api/admin/plans", new { name = "Whatever", monthlyPrice = 5, brand = "No Such Brand" });
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
}
