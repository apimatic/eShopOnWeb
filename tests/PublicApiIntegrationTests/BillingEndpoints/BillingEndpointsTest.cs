using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.BillingEndpoints;

/// <summary>
/// These run against the real Maxio sandbox: the app reads its Maxio:* settings from user-secrets
/// (or Maxio__* environment variables). Nothing is stubbed.
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

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    [TestMethod]
    public async Task PlansRequireSignIn()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListsPlansForShopper()
    {
        var response = await ClientFor(ApiTokenHelper.GetNormalUserToken()).GetAsync("api/plans");
        response.EnsureSuccessStatusCode();
        var body = await ReadJson(response);
        Assert.IsTrue(body.GetProperty("plans").GetArrayLength() > 0);
    }

    [TestMethod]
    public async Task ShopperCannotUseStaffEndpoints()
    {
        var client = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var created = await client.PostAsJsonAsync("api/admin/plans", new { name = "x", price = 1, brandId = 1 });
        var priced = await client.PostAsJsonAsync("api/admin/plans/1/currency-prices", new { currency = "EUR", price = 1 });
        var dated = await client.PutAsJsonAsync("api/admin/subscriptions/1/next-billing-date", new { date = "2030-01-01" });
        Assert.AreEqual(HttpStatusCode.Forbidden, created.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, priced.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, dated.StatusCode);
    }

    [TestMethod]
    public async Task RefusesPlanWithBrandOutsideTheCatalog()
    {
        var client = ClientFor(ApiTokenHelper.GetAdminUserToken());
        var response = await client.PostAsJsonAsync("api/admin/plans", new { name = "No Such Brand", price = 1, brandId = 9999 });
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task RefusesSubscribingToAPlanThatIsNotOffered()
    {
        var client = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var response = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "no-such-plan-handle" });
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task UsageNeedsAValidDate()
    {
        var client = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var response = await client.GetAsync("api/subscriptions/1/usage?date=yesterday");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscriptionLifecycleAndOwnership()
    {
        var shopper = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var plans = (await ReadJson(await shopper.GetAsync("api/plans"))).GetProperty("plans")
            .EnumerateArray().Select(p => p.GetProperty("handle").GetString()!).ToList();
        Assert.IsTrue(plans.Count >= 2, "the sandbox needs at least two offered plans");

        // subscribe, and hold a second subscription at the same time
        var created = await shopper.PostAsJsonAsync("api/subscriptions", new { planHandle = plans[0] });
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        var subscription = await ReadJson(created);
        var id = subscription.GetProperty("id").GetInt32();
        Assert.AreEqual(plans[0], subscription.GetProperty("planHandle").GetString());
        var second = await shopper.PostAsJsonAsync("api/subscriptions", new { planHandle = plans[1] });
        Assert.AreEqual(HttpStatusCode.Created, second.StatusCode);

        // the page reports what is owed
        var page = await ReadJson(await shopper.GetAsync($"api/subscriptions/{id}"));
        Assert.AreEqual(page.GetProperty("balance").GetDecimal() <= 0, page.GetProperty("paid").GetBoolean());

        // another user cannot see it
        var other = ClientFor(ApiTokenHelper.GetAdminUserToken());
        Assert.AreEqual(HttpStatusCode.NotFound, (await other.GetAsync($"api/subscriptions/{id}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await other.PostAsync($"api/subscriptions/{id}/cancel", null)).StatusCode);

        // pause / resume
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"api/subscriptions/{id}/resume", null)).StatusCode);
        var paused = await shopper.PostAsync($"api/subscriptions/{id}/pause", null);
        paused.EnsureSuccessStatusCode();
        Assert.AreEqual("on_hold", (await ReadJson(paused)).GetProperty("state").GetString());
        var resumed = await shopper.PostAsync($"api/subscriptions/{id}/resume", null);
        resumed.EnsureSuccessStatusCode();
        Assert.AreEqual("active", (await ReadJson(resumed)).GetProperty("state").GetString());

        // cancel, switch while cancelled, restart
        var cancelled = await shopper.PostAsync($"api/subscriptions/{id}/cancel", null);
        cancelled.EnsureSuccessStatusCode();
        Assert.AreEqual("canceled", (await ReadJson(cancelled)).GetProperty("state").GetString());
        var switched = await shopper.PostAsJsonAsync($"api/subscriptions/{id}/switch", new { planHandle = plans[1] });
        switched.EnsureSuccessStatusCode();
        Assert.AreEqual(plans[1], (await ReadJson(switched)).GetProperty("planHandle").GetString());
        var restarted = await shopper.PostAsync($"api/subscriptions/{id}/restart", null);
        restarted.EnsureSuccessStatusCode();
        Assert.AreEqual("active", (await ReadJson(restarted)).GetProperty("state").GetString());
    }

    [TestMethod]
    public void MaxioSettingsRefuseBlankCredentials()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new[]
        {
            new System.Collections.Generic.KeyValuePair<string, string?>("Maxio:ApiKey", " "),
            new System.Collections.Generic.KeyValuePair<string, string?>("Maxio:Subdomain", "x"),
            new System.Collections.Generic.KeyValuePair<string, string?>("Maxio:Environment", "US"),
        }).Build();
        Assert.ThrowsException<InvalidOperationException>(() => MaxioSettings.Load(config));
    }

    [TestMethod]
    public async Task ReportsAnUnreachableBillingSystemAsAGatewayError()
    {
        // BaseUrl is used verbatim, so pointing it at a closed port exercises the connection-failure path
        var settings = new MaxioSettings
        {
            ApiKey = "unused", Subdomain = "unused", Environment = "US", BaseUrl = "http://127.0.0.1:9",
        };
        var service = new MaxioBillingService(MaxioServiceCollectionExtensions.CreateClient(settings),
            NullLogger<MaxioBillingService>.Instance);
        var ex = await Assert.ThrowsExceptionAsync<BillingException>(() => service.ListPlansAsync(CancellationToken.None));
        Assert.AreEqual(HttpStatusCode.BadGateway, ex.StatusCode);
    }
}
