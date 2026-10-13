using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Walmart;

[TestClass]
public class WalmartStockEndpointsTests
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
    public async Task SetAndGetStock_AdminRoundTrips()
    {
        var client = Client(ApiTokenHelper.GetAdminUserToken());

        var put = await client.PutAsync("api/walmart/stock/ESHOP-7", Json("""{ "quantity": 33 }"""));
        put.EnsureSuccessStatusCode();
        using var putDoc = JsonDocument.Parse(await put.Content.ReadAsStringAsync());
        Assert.AreEqual("ESHOP-7", putDoc.RootElement.GetProperty("sku").GetString());
        Assert.AreEqual(33, putDoc.RootElement.GetProperty("quantity").GetInt32());

        var get = await client.GetAsync("api/walmart/stock/ESHOP-7");
        get.EnsureSuccessStatusCode();
        using var getDoc = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        Assert.AreEqual("ESHOP-7", getDoc.RootElement.GetProperty("sku").GetString());
        Assert.AreEqual(33, getDoc.RootElement.GetProperty("quantity").GetInt32());
    }

    [TestMethod]
    public async Task Sync_AdminReturnsResultPerSku()
    {
        var client = Client(ApiTokenHelper.GetAdminUserToken());

        var resp = await client.PostAsync("api/walmart/stock/sync", Json("{}"));
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var results = doc.RootElement.GetProperty("results");
        Assert.IsTrue(results.GetArrayLength() > 0);
        foreach (var r in results.EnumerateArray())
        {
            StringAssert.StartsWith(r.GetProperty("sku").GetString(), "ESHOP-");
            Assert.AreEqual("updated", r.GetProperty("status").GetString());
        }
    }

    [TestMethod]
    public async Task Report_AdminLinesUpBothSides()
    {
        var client = Client(ApiTokenHelper.GetAdminUserToken());

        // Push eShop levels to the (fake) Walmart first so both sides are populated.
        await client.PostAsync("api/walmart/stock/sync", Json("{}"));

        var resp = await client.GetAsync("api/walmart/stock");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.IsFalse(doc.RootElement.GetProperty("truncated").GetBoolean());
        var items = doc.RootElement.GetProperty("items");
        Assert.IsTrue(items.GetArrayLength() > 0);
        var first = items[0];
        Assert.IsTrue(first.TryGetProperty("sku", out _));
        Assert.IsTrue(first.TryGetProperty("walmartQuantity", out _));
        Assert.IsTrue(first.TryGetProperty("eshopQuantity", out _));
    }

    [DataTestMethod]
    [DataRow("GET", "api/walmart/stock")]
    [DataRow("GET", "api/walmart/stock/ESHOP-7")]
    [DataRow("PUT", "api/walmart/stock/ESHOP-7")]
    [DataRow("POST", "api/walmart/stock/sync")]
    public async Task NormalUser_IsForbidden(string method, string path)
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is "PUT" or "POST")
        {
            request.Content = Json("""{ "quantity": 1 }""");
        }

        var resp = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [TestMethod]
    public async Task NoToken_IsUnauthorized()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("api/walmart/stock");
        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
