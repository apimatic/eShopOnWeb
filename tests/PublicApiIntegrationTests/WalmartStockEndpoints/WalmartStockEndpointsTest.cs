using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.WalmartStockEndpoints;

[TestClass]
public class WalmartStockEndpointsTest
{
    private static HttpClient AdminClient(WalmartApiFactory f) => Authed(f, ApiTokenHelper.GetAdminUserToken());
    private static HttpClient NormalClient(WalmartApiFactory f) => Authed(f, ApiTokenHelper.GetNormalUserToken());

    private static HttpClient Authed(WalmartApiFactory f, string token)
    {
        var client = f.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [TestMethod]
    public async Task SetStock_NormalUser_Forbidden()
    {
        using var f = new WalmartApiFactory();
        var response = await NormalClient(f).PutAsJsonAsync("api/walmart/stock/ESHOP-7", new { quantity = 5 });
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task SetStock_Unauthenticated_Unauthorized()
    {
        using var f = new WalmartApiFactory();
        var response = await f.CreateClient().PutAsJsonAsync("api/walmart/stock/ESHOP-7", new { quantity = 5 });
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SetThenGetStock_AsAdmin_ReflectsWalmartRecord()
    {
        using var f = new WalmartApiFactory();
        var client = AdminClient(f);

        var put = await client.PutAsJsonAsync("api/walmart/stock/ESHOP-7", new { quantity = 11 });
        put.EnsureSuccessStatusCode();
        var putDoc = JsonDocument.Parse(await put.Content.ReadAsStringAsync());
        Assert.AreEqual("ESHOP-7", putDoc.RootElement.GetProperty("sku").GetString());
        Assert.AreEqual(11, putDoc.RootElement.GetProperty("quantity").GetInt32());

        var get = await client.GetAsync("api/walmart/stock/ESHOP-7");
        get.EnsureSuccessStatusCode();
        var getDoc = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        Assert.AreEqual("ESHOP-7", getDoc.RootElement.GetProperty("sku").GetString());
        Assert.AreEqual(11, getDoc.RootElement.GetProperty("quantity").GetInt32());
    }

    [TestMethod]
    public async Task GetStock_UnknownSku_NotFound()
    {
        using var f = new WalmartApiFactory();
        var response = await AdminClient(f).GetAsync("api/walmart/stock/ESHOP-does-not-exist");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task Sync_AsAdmin_ReturnsResultPerCatalogSku()
    {
        using var f = new WalmartApiFactory();
        var response = await AdminClient(f).PostAsync("api/walmart/stock/sync", content: null);
        response.EnsureSuccessStatusCode();

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var results = doc.RootElement.GetProperty("results");
        // One entry per seeded catalog item (12).
        Assert.AreEqual(12, results.GetArrayLength());
        foreach (var r in results.EnumerateArray())
        {
            Assert.IsTrue(r.GetProperty("sku").GetString()!.StartsWith("ESHOP-"));
            Assert.AreEqual("updated", r.GetProperty("status").GetString());
        }
    }

    [TestMethod]
    public async Task Sync_NormalUser_Forbidden()
    {
        using var f = new WalmartApiFactory();
        var response = await NormalClient(f).PostAsync("api/walmart/stock/sync", content: null);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task Report_AsAdmin_LinesUpWalmartAndEshop_AndShowsDivergence()
    {
        using var f = new WalmartApiFactory();
        var client = AdminClient(f);

        // eShop holds 5 of item 1; push the whole catalog to Walmart so the two sides agree.
        await client.PutAsJsonAsync("api/catalog-items/1/stock", new { quantity = 5 });
        await client.PostAsync("api/walmart/stock/sync", content: null);

        // Now move Walmart's record for ESHOP-1 out of step with eShop.
        await client.PutAsJsonAsync("api/walmart/stock/ESHOP-1", new { quantity = 99 });

        var report = await client.GetAsync("api/walmart/stock");
        report.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await report.Content.ReadAsStringAsync());
        var items = doc.RootElement.GetProperty("items");

        JsonElement? eshop1 = null;
        foreach (var item in items.EnumerateArray())
        {
            if (item.GetProperty("sku").GetString() == "ESHOP-1")
            {
                eshop1 = item;
            }
        }

        Assert.IsTrue(eshop1.HasValue, "report should contain ESHOP-1");
        Assert.AreEqual(99, eshop1!.Value.GetProperty("walmartQuantity").GetInt32());
        Assert.AreEqual(5, eshop1!.Value.GetProperty("eshopQuantity").GetInt32());
        // All 12 catalog SKUs are present in the report.
        Assert.AreEqual(12, items.GetArrayLength());
    }

    [TestMethod]
    public async Task Report_NormalUser_Forbidden()
    {
        using var f = new WalmartApiFactory();
        var response = await NormalClient(f).GetAsync("api/walmart/stock");
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
