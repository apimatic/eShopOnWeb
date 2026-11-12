using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.BillingEndpoints;

/// <summary>
/// Access rules and request validation of the billing endpoints. None of these requests reaches Maxio.
/// The Maxio settings come from the "Maxio:" configuration (user-secrets), the same way the app reads them.
/// </summary>
[TestClass]
public class BillingEndpointsTest
{
    private static HttpClient ClientFor(string? token)
    {
        var client = ProgramTest.NewClient;
        if (token != null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    [DataTestMethod]
    [DataRow("GET", "api/plans")]
    [DataRow("POST", "api/subscriptions")]
    [DataRow("GET", "api/subscriptions/1")]
    [DataRow("POST", "api/subscriptions/1/pause")]
    [DataRow("POST", "api/subscriptions/1/resume")]
    [DataRow("POST", "api/subscriptions/1/cancel")]
    [DataRow("POST", "api/subscriptions/1/restart")]
    [DataRow("POST", "api/subscriptions/1/switch")]
    [DataRow("GET", "api/subscriptions/1/usage?date=2026-01-01")]
    [DataRow("POST", "api/admin/plans")]
    [DataRow("POST", "api/admin/plans/1/currency-prices")]
    [DataRow("PUT", "api/admin/subscriptions/1/next-billing-date")]
    public async Task ReturnsUnauthorizedWithoutToken(string method, string url)
    {
        var response = await ClientFor(null).SendAsync(new HttpRequestMessage(new HttpMethod(method), url) { Content = Json("{}") });
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [DataTestMethod]
    [DataRow("POST", "api/admin/plans")]
    [DataRow("POST", "api/admin/plans/1/currency-prices")]
    [DataRow("PUT", "api/admin/subscriptions/1/next-billing-date")]
    public async Task StaffRoutesAreForbiddenForShoppers(string method, string url)
    {
        var client = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url) { Content = Json("{}") });
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task NewPlanWithoutNameIsRejected()
    {
        var response = await ClientFor(ApiTokenHelper.GetAdminUserToken()).PostAsync("api/admin/plans", Json("{\"monthlyPrice\":5,\"brandId\":1}"));
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task NewPlanWithUnknownBrandIsRejected()
    {
        var response = await ClientFor(ApiTokenHelper.GetAdminUserToken()).PostAsync("api/admin/plans", Json("{\"name\":\"x\",\"monthlyPrice\":5,\"brandId\":9999}"));
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task UsageWithMalformedDateIsRejected()
    {
        var response = await ClientFor(ApiTokenHelper.GetNormalUserToken()).GetAsync("api/subscriptions/1/usage?date=12/31/2026");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task NextBillingDateWithoutDateIsRejected()
    {
        var response = await ClientFor(ApiTokenHelper.GetAdminUserToken()).PutAsync("api/admin/subscriptions/1/next-billing-date", Json("{}"));
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task CurrencyPriceWithBadCurrencyIsRejected()
    {
        var response = await ClientFor(ApiTokenHelper.GetAdminUserToken()).PostAsync("api/admin/plans/1/currency-prices", Json("{\"currency\":\"EURO\",\"price\":10}"));
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
