using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.WalmartStockEndpoints;

[TestClass]
public class WalmartStockEndpointsTest
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static HttpClient AdminClient(WalmartTestFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetAdminUserToken());
        return client;
    }

    [TestMethod]
    public async Task SetStock_AsAdmin_ReturnsSkuAndQuantity()
    {
        using var factory = new WalmartTestFactory();
        var client = AdminClient(factory);

        var body = new StringContent("{\"quantity\":42}", Encoding.UTF8, "application/json");
        var response = await client.PutAsync("api/walmart/stock/ESHOP-7", body);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual("ESHOP-7", doc.RootElement.GetProperty("sku").GetString());
        Assert.AreEqual(42, doc.RootElement.GetProperty("quantity").GetInt32());
        Assert.AreEqual(42, factory.Walmart.Store["ESHOP-7"]);
    }

    [TestMethod]
    public async Task SetStock_AsNormalUser_IsForbidden()
    {
        using var factory = new WalmartTestFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var body = new StringContent("{\"quantity\":1}", Encoding.UTF8, "application/json");
        var response = await client.PutAsync("api/walmart/stock/ESHOP-7", body);

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task SetStock_Unauthenticated_IsUnauthorized()
    {
        using var factory = new WalmartTestFactory();
        var client = factory.CreateClient();

        var body = new StringContent("{\"quantity\":1}", Encoding.UTF8, "application/json");
        var response = await client.PutAsync("api/walmart/stock/ESHOP-7", body);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task GetStock_ReflectsWhatWalmartRecords()
    {
        using var factory = new WalmartTestFactory();
        var client = AdminClient(factory);

        await client.PutAsync("api/walmart/stock/ESHOP-3", new StringContent("{\"quantity\":15}", Encoding.UTF8, "application/json"));

        var response = await client.GetAsync("api/walmart/stock/ESHOP-3");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual("ESHOP-3", doc.RootElement.GetProperty("sku").GetString());
        Assert.AreEqual(15, doc.RootElement.GetProperty("quantity").GetInt32());
    }

    [TestMethod]
    public async Task GetStock_UnknownSku_IsNotFound()
    {
        using var factory = new WalmartTestFactory();
        var client = AdminClient(factory);

        var response = await client.GetAsync("api/walmart/stock/ESHOP-does-not-exist");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task Sync_ReturnsPerSkuResults()
    {
        using var factory = new WalmartTestFactory();
        var client = AdminClient(factory);

        var response = await client.PostAsync("api/walmart/stock/sync", content: null);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var results = doc.RootElement.GetProperty("results");
        Assert.IsTrue(results.GetArrayLength() > 0, "sync should report at least one SKU");
        foreach (var entry in results.EnumerateArray())
        {
            Assert.IsTrue(entry.TryGetProperty("sku", out _));
            var status = entry.GetProperty("status").GetString();
            Assert.IsTrue(status == "updated" || status == "failed");
        }

        // After a sync, Walmart's store holds every catalog SKU.
        Assert.IsTrue(factory.Walmart.Store.Count > 0);
    }

    [TestMethod]
    public async Task Report_ShowsBothSidesAndOnlyOneSideSkus()
    {
        using var factory = new WalmartTestFactory();
        var client = AdminClient(factory);

        // A Walmart-only SKU that maps to no eShop catalog item.
        await client.PutAsync("api/walmart/stock/ESHOP-999999", new StringContent("{\"quantity\":7}", Encoding.UTF8, "application/json"));

        var response = await client.GetAsync("api/walmart/stock");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.GetProperty("items");

        bool foundWalmartOnly = false;
        bool foundEshopOnly = false;
        foreach (var entry in items.EnumerateArray())
        {
            var sku = entry.GetProperty("sku").GetString();
            var walmart = entry.GetProperty("walmartQuantity");
            var eshop = entry.GetProperty("eshopQuantity");

            if (sku == "ESHOP-999999")
            {
                foundWalmartOnly = true;
                Assert.AreEqual(7, walmart.GetInt32());
                Assert.AreEqual(JsonValueKind.Null, eshop.ValueKind);
            }
            // An eShop catalog SKU that was never set on Walmart shows walmart=null.
            if (walmart.ValueKind == JsonValueKind.Null && eshop.ValueKind != JsonValueKind.Null)
            {
                foundEshopOnly = true;
            }
        }

        Assert.IsTrue(foundWalmartOnly, "expected the Walmart-only SKU in the report");
        Assert.IsTrue(foundEshopOnly, "expected at least one eShop-only SKU (catalog item not yet on Walmart)");
    }
}
