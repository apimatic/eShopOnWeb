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
/// Tests of the Maxio billing endpoints. Those that reach Maxio run against the Maxio sandbox, using the
/// credentials of the MAXIO_API_KEY / MAXIO_SITE_SUBDOMAIN / MAXIO_ENVIRONMENT environment variables
/// (mapped onto the Maxio: configuration section); they are skipped when those variables are not set.
/// </summary>
[TestClass]
public class BillingEndpointsTests
{
    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        MapEnvironment("MAXIO_API_KEY", "Maxio__ApiKey");
        MapEnvironment("MAXIO_SITE_SUBDOMAIN", "Maxio__Subdomain");
        MapEnvironment("MAXIO_ENVIRONMENT", "Maxio__Environment");
    }

    private static void MapEnvironment(string from, string to)
    {
        var value = Environment.GetEnvironmentVariable(from);
        if (!string.IsNullOrEmpty(value) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(to)))
        {
            Environment.SetEnvironmentVariable(to, value);
        }
    }

    private static HttpClient Client(string token)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static void RequireSandbox()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MAXIO_API_KEY")))
        {
            Assert.Inconclusive("The Maxio sandbox credentials are not available.");
        }
    }

    [TestMethod]
    public async Task PlansRequireSignIn()
    {
        var response = await ProgramTest.NewClient.GetAsync("/api/plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task StaffEndpointsRejectShoppers()
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());
        var response = await client.PostAsJsonAsync("/api/admin/plans", new { name = "x", monthlyPrice = 1, brand = "Azure" });
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);

        response = await client.PutAsJsonAsync("/api/admin/subscriptions/1/next-billing-date", new { date = "2099-01-01" });
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeNeedsAPlanHandle()
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());
        var response = await client.PostAsJsonAsync("/api/subscriptions", new { });
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task UsageNeedsAValidDate()
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());
        var response = await client.GetAsync("/api/subscriptions/1/usage?date=yesterday");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task PlanNeedsACatalogBrand()
    {
        var client = Client(ApiTokenHelper.GetAdminUserToken());
        var response = await client.PostAsJsonAsync("/api/admin/plans",
            new { name = "Plan " + Guid.NewGuid().ToString("N")[..6], monthlyPrice = 9.99, brand = "Not A Brand" });
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task ShopperRunsASubscriptionLifecycleAgainstTheSandbox()
    {
        RequireSandbox();
        var admin = Client(ApiTokenHelper.GetAdminUserToken());
        var shopper = Client(ApiTokenHelper.GetNormalUserToken());

        // staff add a plan, which a shopper then sees and subscribes to
        var planName = "Test Plan " + Guid.NewGuid().ToString("N")[..8];
        var created = await admin.PostAsJsonAsync("/api/admin/plans", new { name = planName, monthlyPrice = 12.34, brand = "Azure" });
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());
        var plan = await Json(created);
        var handle = plan.GetProperty("handle").GetString();

        var plans = await Json(await shopper.GetAsync("/api/plans"));
        Assert.IsTrue(plans.GetProperty("plans").EnumerateArray().Any(p => p.GetProperty("handle").GetString() == handle));

        var subscribed = await shopper.PostAsJsonAsync("/api/subscriptions", new { planHandle = handle });
        Assert.AreEqual(HttpStatusCode.Created, subscribed.StatusCode, await subscribed.Content.ReadAsStringAsync());
        var id = (await Json(subscribed)).GetProperty("id").GetInt32();

        var page = await Json(await shopper.GetAsync($"/api/subscriptions/{id}"));
        Assert.AreEqual("active", page.GetProperty("state").GetString());
        Assert.IsFalse(page.GetProperty("paid").GetBoolean(), "the first invoice is still to be paid");

        Assert.AreEqual(HttpStatusCode.OK, (await shopper.PostAsync($"/api/subscriptions/{id}/pause", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await shopper.PostAsync($"/api/subscriptions/{id}/resume", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"/api/subscriptions/{id}/resume", null)).StatusCode);

        Assert.AreEqual(HttpStatusCode.OK, (await shopper.PostAsync($"/api/subscriptions/{id}/cancel", null)).StatusCode);
        var switched = await shopper.PostAsJsonAsync($"/api/subscriptions/{id}/switch", new { planHandle = "basic-plan" });
        Assert.AreEqual(HttpStatusCode.OK, switched.StatusCode, await switched.Content.ReadAsStringAsync());
        Assert.AreEqual("basic-plan", (await Json(switched)).GetProperty("planHandle").GetString());
        Assert.AreEqual(HttpStatusCode.OK, (await shopper.PostAsync($"/api/subscriptions/{id}/restart", null)).StatusCode);

        // somebody else's subscription is not visible
        var other = await admin.GetAsync($"/api/subscriptions/{id}");
        Assert.AreEqual(HttpStatusCode.NotFound, other.StatusCode);

        // staff move the next billing date
        var moved = await admin.PutAsJsonAsync($"/api/admin/subscriptions/{id}/next-billing-date", new { date = "2099-01-15" });
        Assert.AreEqual(HttpStatusCode.OK, moved.StatusCode, await moved.Content.ReadAsStringAsync());
        Assert.IsTrue((await Json(moved)).GetProperty("nextBillingAt").GetString()!.StartsWith("2099-01-15"));

        var usage = await Json(await shopper.GetAsync($"/api/subscriptions/{id}/usage?date={DateTime.UtcNow:yyyy-MM-dd}"));
        Assert.AreEqual(0m, usage.GetProperty("totalQuantity").GetDecimal());
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>());
}
