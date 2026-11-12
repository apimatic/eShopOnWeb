using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests;

/// <summary>
/// Calls the real Maxio sandbox through the PublicApi (settings come from the Maxio user-secrets). Nothing is stubbed.
/// </summary>
[TestClass]
public class MaxioEndpoints
{
    private static HttpClient Client(string token)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [TestMethod]
    public async Task AdminRoutesRejectShoppers()
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());
        var response = await client.PostAsJsonAsync("api/admin/plans", new { name = "x", monthlyPrice = 1, brandId = 1 });
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        response = await client.PutAsJsonAsync("api/admin/subscriptions/1/next-billing-date", new { date = "2999-01-01" });
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task PlansRequireSignIn()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task NewPlanNeedsABrandFromTheCatalog()
    {
        var client = Client(ApiTokenHelper.GetAdminUserToken());
        var response = await client.PostAsJsonAsync("api/admin/plans", new { name = "Ghost", monthlyPrice = 5, brandId = 999999 });
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribingToAnUnofferedPlanIsRefused()
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());
        var response = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "not-a-plan-we-offer" });
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task UsageNeedsAValidDate()
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());
        var response = await client.GetAsync("api/subscriptions/1/usage?date=12-11-2026");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscriptionLifecycleAgainstSandbox()
    {
        var admin = Client(ApiTokenHelper.GetAdminUserToken());
        var shopper = Client(ApiTokenHelper.GetNormalUserToken());
        var stranger = Client(ApiTokenHelper.GetAdminUserToken()); // another identity: the admin is not the owner

        var unique = System.Guid.NewGuid().ToString("N")[..8];
        var created = await admin.PostAsJsonAsync("api/admin/plans", new { name = $"Test {unique}", monthlyPrice = 3.25, brand = ".NET" });
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());
        var handle = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("handle").GetString();

        var plans = await shopper.GetStringAsync("api/plans");
        StringAssert.Contains(plans, handle);

        var sub = await shopper.PostAsJsonAsync("api/subscriptions", new { planHandle = handle });
        Assert.AreEqual(HttpStatusCode.Created, sub.StatusCode, await sub.Content.ReadAsStringAsync());
        var id = JsonDocument.Parse(await sub.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetInt32();

        var again = await shopper.PostAsJsonAsync("api/subscriptions", new { planHandle = handle });
        Assert.AreEqual(HttpStatusCode.Conflict, again.StatusCode);

        var page = JsonDocument.Parse(await shopper.GetStringAsync($"api/subscriptions/{id}")).RootElement;
        Assert.IsTrue(page.TryGetProperty("paid", out _));

        Assert.AreEqual(HttpStatusCode.NotFound, (await stranger.GetAsync($"api/subscriptions/{id}")).StatusCode);

        Assert.AreEqual(HttpStatusCode.OK, (await shopper.PostAsync($"api/subscriptions/{id}/pause", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await shopper.PostAsync($"api/subscriptions/{id}/resume", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await shopper.PostAsync($"api/subscriptions/{id}/cancel", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await shopper.PostAsync($"api/subscriptions/{id}/restart", null)).StatusCode);

        var day = System.DateTime.UtcNow.ToString("yyyy-MM-dd");
        var usage = await shopper.GetAsync($"api/subscriptions/{id}/usage?date={day}");
        Assert.AreEqual(HttpStatusCode.OK, usage.StatusCode);

        var tomorrow = System.DateTime.UtcNow.AddDays(10).ToString("yyyy-MM-dd");
        var nbd = await admin.PutAsJsonAsync($"api/admin/subscriptions/{id}/next-billing-date", new { date = tomorrow });
        Assert.AreEqual(HttpStatusCode.OK, nbd.StatusCode, await nbd.Content.ReadAsStringAsync());

        await shopper.PostAsync($"api/subscriptions/{id}/cancel", null);
    }
}
