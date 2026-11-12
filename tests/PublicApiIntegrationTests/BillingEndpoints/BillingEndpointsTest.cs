using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.BillingEndpoints;

/// <summary>
/// These run against the Maxio sandbox (the Maxio:* settings come from user-secrets); nothing is stubbed.
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

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    [TestMethod]
    public async Task PlansRequireSignIn()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task StaffRoutesRejectShoppers()
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());
        var response = await client.PostAsync("api/admin/plans", Json("{\"name\":\"x\",\"price\":1,\"brand\":\"Azure\"}"));
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task ListsOnlyPlansOfCatalogBrands()
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());
        var response = await client.GetAsync("api/plans");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var brands = new[] { "Azure", ".NET", "Visual Studio", "SQL Server", "Other" };
        foreach (var plan in doc.RootElement.GetProperty("plans").EnumerateArray())
            CollectionAssert.Contains(brands, plan.GetProperty("brand").GetString());
    }

    [TestMethod]
    public async Task UnknownPlanCannotBeSubscribedTo()
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());
        var response = await client.PostAsync("api/subscriptions", Json("{\"planHandle\":\"no-such-plan-handle\"}"));
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "not a plan offered");
    }

    [TestMethod]
    public async Task PlanBrandMustBeInTheCatalog()
    {
        var client = Client(ApiTokenHelper.GetAdminUserToken());
        var response = await client.PostAsync("api/admin/plans", Json("{\"name\":\"x\",\"price\":1,\"brand\":\"No such brand\"}"));
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task UsageDateMustBeValid()
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());
        var response = await client.GetAsync("api/subscriptions/1/usage?date=2026-13-45");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task ShopperCannotSeeSomeoneElsesSubscription()
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());
        var response = await client.GetAsync("api/subscriptions/1");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }
}
