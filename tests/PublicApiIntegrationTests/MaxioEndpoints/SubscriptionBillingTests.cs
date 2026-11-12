using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.MaxioEndpoints;

/// <summary>Runs against the real Maxio sandbox configured through the Maxio: settings (user-secrets).</summary>
[TestClass]
public class SubscriptionBillingTests
{
    private static HttpClient Client(string? token)
    {
        var c = ProgramTest.NewClient;
        if (token != null) c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    [TestMethod]
    public async Task PlansRequireSignIn()
    {
        var r = await Client(null).GetAsync("/api/plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [TestMethod]
    public async Task ShopperCannotUseStaffEndpoints()
    {
        var c = Client(ApiTokenHelper.GetNormalUserToken());
        var r = await c.PostAsJsonAsync("/api/admin/plans", new { name = "x", monthlyPrice = 1, catalogBrandId = 1 });
        Assert.AreEqual(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [TestMethod]
    public async Task AdminPlanWithUnknownBrandIsRefused()
    {
        var c = Client(ApiTokenHelper.GetAdminUserToken());
        var r = await c.PostAsJsonAsync("/api/admin/plans", new { name = "Ghost", monthlyPrice = 3, catalogBrandId = 987654 });
        Assert.AreEqual(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [TestMethod]
    public async Task ShopperListsPlans()
    {
        var r = await Client(ApiTokenHelper.GetNormalUserToken()).GetAsync("/api/plans");
        Assert.AreEqual(HttpStatusCode.OK, r.StatusCode);
    }

    [TestMethod]
    public async Task UnknownSubscriptionIsNotFound()
    {
        var r = await Client(ApiTokenHelper.GetNormalUserToken()).GetAsync("/api/subscriptions/1");
        Assert.AreEqual(HttpStatusCode.NotFound, r.StatusCode);
    }

    [TestMethod]
    public async Task UsageNeedsARealDate()
    {
        var r = await Client(ApiTokenHelper.GetNormalUserToken()).GetAsync("/api/subscriptions/1/usage?date=2026-02-30");
        Assert.IsTrue(r.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound);
    }
}
