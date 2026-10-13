using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.WalmartEndpoints;

/// <summary>
/// Every Walmart stock endpoint, and the eShop catalog-item stock endpoint, is an operator action
/// restricted to the administrator role. A normal signed-in user must be refused (403) before any
/// Walmart call is ever attempted.
/// </summary>
[TestClass]
public class WalmartStockAuthorizationTest
{
    private static HttpClient NormalUserClient()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        return client;
    }

    private static HttpClient AnonymousClient() => ProgramTest.NewClient;

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    [TestMethod]
    public async Task PutWalmartStockForbiddenForNormalUser()
    {
        var response = await NormalUserClient().PutAsync("api/walmart/stock/ESHOP-1", Json("{\"quantity\":5}"));
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task GetWalmartStockForbiddenForNormalUser()
    {
        var response = await NormalUserClient().GetAsync("api/walmart/stock/ESHOP-1");
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task SyncForbiddenForNormalUser()
    {
        var response = await NormalUserClient().PostAsync("api/walmart/stock/sync", Json("{}"));
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task ReportForbiddenForNormalUser()
    {
        var response = await NormalUserClient().GetAsync("api/walmart/stock");
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task SetCatalogItemStockForbiddenForNormalUser()
    {
        var response = await NormalUserClient().PutAsync("api/catalog-items/1/stock", Json("{\"quantity\":5}"));
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task ReportRequiresAuthentication()
    {
        var response = await AnonymousClient().GetAsync("api/walmart/stock");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
