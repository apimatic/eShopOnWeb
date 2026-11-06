using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Runs against the Maxio sandbox configured through the "Maxio:" settings (user-secrets).
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

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    [TestMethod]
    public async Task ShopperEndpointsRequireAToken()
    {
        var client = ProgramTest.NewClient;
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/plans")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/subscriptions/1")).StatusCode);
    }

    [TestMethod]
    public async Task StaffEndpointsRejectShoppers()
    {
        var client = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var plan = await client.PostAsJsonAsync("api/admin/plans", new { name = "x", price = 1, brand = "Azure" });
        var date = await client.PutAsJsonAsync("api/admin/subscriptions/1/next-billing-date", new { date = "2030-01-01" });
        var currency = await client.PostAsJsonAsync("api/admin/plans/1/currency-prices", new { currency = "EUR", price = 5 });

        Assert.AreEqual(HttpStatusCode.Forbidden, plan.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, date.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, currency.StatusCode);
    }

    [TestMethod]
    public async Task NewPlanMustUseACatalogBrand()
    {
        var client = ClientFor(ApiTokenHelper.GetAdminUserToken());
        var response = await client.PostAsJsonAsync("api/admin/plans", new { name = "Brandless", price = 5, brand = "No Such Brand" });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task UsageNeedsAValidDate()
    {
        var client = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var response = await client.GetAsync("api/subscriptions/1/usage?date=yesterday");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task ShopperCanSubscribeAndManageASubscription()
    {
        var client = ClientFor(ApiTokenHelper.GetNormalUserToken());

        var plansResponse = await client.GetAsync("api/plans");
        plansResponse.EnsureSuccessStatusCode();
        var plans = (await ReadJson(plansResponse)).GetProperty("plans");
        Assert.IsTrue(plans.GetArrayLength() >= 2, "the sandbox should offer at least two plans");
        var first = plans[0].GetProperty("handle").GetString();
        var second = plans[1].GetProperty("handle").GetString();

        var created = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = first });
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        var id = (await ReadJson(created)).GetProperty("id").GetInt32();

        var page = await ReadJson(await client.GetAsync($"api/subscriptions/{id}"));
        Assert.IsFalse(page.GetProperty("paid").GetBoolean(), "a fresh invoiced subscription is unpaid");

        var paused = await ReadJson(await client.PostAsync($"api/subscriptions/{id}/pause", null));
        Assert.AreEqual("on_hold", paused.GetProperty("state").GetString());
        var resumed = await ReadJson(await client.PostAsync($"api/subscriptions/{id}/resume", null));
        Assert.AreEqual("active", resumed.GetProperty("state").GetString());

        var cancelled = await ReadJson(await client.PostAsync($"api/subscriptions/{id}/cancel", null));
        Assert.AreEqual("canceled", cancelled.GetProperty("state").GetString());

        // switching a cancelled subscription puts the shopper on the new plan
        var switched = await ReadJson(await client.PostAsJsonAsync($"api/subscriptions/{id}/switch", new { planHandle = second }));
        Assert.AreEqual("active", switched.GetProperty("state").GetString());
        Assert.AreEqual(second, switched.GetProperty("plan").GetProperty("handle").GetString());

        // another shopper cannot see it
        var other = ClientFor(ApiTokenHelper.GetAdminUserToken());
        Assert.AreEqual(HttpStatusCode.NotFound, (await other.GetAsync($"api/subscriptions/{id}")).StatusCode);

        await client.PostAsync($"api/subscriptions/{id}/cancel", null);
    }
}
