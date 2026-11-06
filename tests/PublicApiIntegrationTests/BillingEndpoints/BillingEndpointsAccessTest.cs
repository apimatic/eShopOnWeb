using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.BillingEndpoints;

/// <summary>
/// Checks that don't need Maxio: who may call each billing endpoint, and request validation.
/// </summary>
[TestClass]
public class BillingEndpointsAccessTest
{
    private static HttpClient ClientFor(string? token)
    {
        var client = ProgramTest.NewClient;
        if (token != null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    [TestMethod]
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
    public async Task RequiresAToken(string method, string url)
    {
        var response = await ClientFor(null).SendAsync(new HttpRequestMessage(new HttpMethod(method), url) { Content = Json("{}") });
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    [DataRow("POST", "api/admin/plans")]
    [DataRow("POST", "api/admin/plans/1/currency-prices")]
    [DataRow("PUT", "api/admin/subscriptions/1/next-billing-date")]
    public async Task StaffEndpointsRefuseShoppers(string method, string url)
    {
        var client = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url) { Content = Json("{}") });
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task UsageRequiresAValidDate()
    {
        var client = ClientFor(ApiTokenHelper.GetNormalUserToken());
        foreach (var url in new[] { "api/subscriptions/1/usage", "api/subscriptions/1/usage?date=nope", "api/subscriptions/1/usage?date=2026-13-40" })
        {
            var response = await client.GetAsync(url);
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, url);
        }
    }

    [TestMethod]
    public async Task NextBillingDateRequiresAValidDate()
    {
        var client = ClientFor(ApiTokenHelper.GetAdminUserToken());
        var response = await client.PutAsync("api/admin/subscriptions/1/next-billing-date", Json("{\"date\":\"20/12/2026\"}"));
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task NewPlanRequiresNameAndPriceAndCatalogBrand()
    {
        var client = ClientFor(ApiTokenHelper.GetAdminUserToken());
        foreach (var body in new[]
                 {
                     "{\"price\":5,\"brandId\":1}",
                     "{\"name\":\"x\",\"brandId\":1}",
                     "{\"name\":\"x\",\"price\":-1,\"brandId\":1}",
                     "{\"name\":\"x\",\"price\":5}",
                     "{\"name\":\"x\",\"price\":5,\"brandId\":99999}",
                 })
        {
            var response = await client.PostAsync("api/admin/plans", Json(body));
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, body);
        }
    }

    [TestMethod]
    public async Task SubscribeRequiresAPlanHandle()
    {
        var client = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var response = await client.PostAsync("api/subscriptions", Json("{}"));
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
