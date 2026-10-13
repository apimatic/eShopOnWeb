using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Walmart;

[TestClass]
public class CatalogItemStockEndpointTests
{
    private WalmartApiFactory _factory = null!;

    [TestInitialize]
    public void Init() => _factory = new WalmartApiFactory();

    [TestCleanup]
    public void Cleanup() => _factory.Dispose();

    private HttpClient Client(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    [TestMethod]
    public async Task Admin_SetsEshopStock()
    {
        var client = Client(ApiTokenHelper.GetAdminUserToken());
        var resp = await client.PutAsync("api/catalog-items/2/stock", Json("""{ "quantity": 15 }"""));
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.AreEqual(2, doc.RootElement.GetProperty("catalogItemId").GetInt32());
        Assert.AreEqual(15, doc.RootElement.GetProperty("quantity").GetInt32());
    }

    [TestMethod]
    public async Task NormalUser_IsForbidden()
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());
        var resp = await client.PutAsync("api/catalog-items/2/stock", Json("""{ "quantity": 5 }"""));
        Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [TestMethod]
    public async Task UnknownItem_IsNotFound()
    {
        var client = Client(ApiTokenHelper.GetAdminUserToken());
        var resp = await client.PutAsync("api/catalog-items/999999/stock", Json("""{ "quantity": 5 }"""));
        Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [TestMethod]
    public async Task NegativeQuantity_IsBadRequest()
    {
        var client = Client(ApiTokenHelper.GetAdminUserToken());
        var resp = await client.PutAsync("api/catalog-items/2/stock", Json("""{ "quantity": -1 }"""));
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}
