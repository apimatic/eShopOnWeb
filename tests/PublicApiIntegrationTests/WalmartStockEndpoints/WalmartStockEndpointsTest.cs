using System.Linq;
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
    private const int CatalogItemId = 1;
    private const string Sku = "ESHOP-1";

    private static HttpClient Admin(WalmartApiTestFactory f)
    {
        var client = f.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetAdminUserToken());
        return client;
    }

    private static HttpClient Shopper(WalmartApiTestFactory f)
    {
        var client = f.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        return client;
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    [TestMethod]
    public async Task SetThenGetWalmartStock_RoundTripsThroughWalmart()
    {
        using var factory = new WalmartApiTestFactory();
        var admin = Admin(factory);

        var put = await admin.PutAsJsonAsync($"api/walmart/stock/{Sku}", new { quantity = 55 });
        var putBody = await ReadJson(put);
        Assert.AreEqual(Sku, putBody.GetProperty("sku").GetString());
        Assert.AreEqual(55, putBody.GetProperty("quantity").GetInt32());

        var get = await admin.GetAsync($"api/walmart/stock/{Sku}");
        var getBody = await ReadJson(get);
        Assert.AreEqual(Sku, getBody.GetProperty("sku").GetString());
        Assert.AreEqual(55, getBody.GetProperty("quantity").GetInt32());
    }

    [TestMethod]
    public async Task SyncPushesEshopLevels_AndReportLinesThemUp()
    {
        using var factory = new WalmartApiTestFactory();
        var admin = Admin(factory);

        // Set eShop's own level for the item.
        await admin.PutAsJsonAsync($"api/catalog-items/{CatalogItemId}/stock", new { quantity = 30 });

        // Push the whole catalog to Walmart.
        var sync = await admin.PostAsync("api/walmart/stock/sync", null);
        var syncBody = await ReadJson(sync);
        var results = syncBody.GetProperty("results").EnumerateArray().ToList();
        Assert.IsTrue(results.Count >= 12, "sync should report every catalog item");
        var ours = results.Single(r => r.GetProperty("sku").GetString() == Sku);
        Assert.AreEqual("updated", ours.GetProperty("status").GetString());

        // Reconciliation report: Walmart and eShop now agree on our SKU.
        var report = await admin.GetAsync("api/walmart/stock");
        var items = (await ReadJson(report)).GetProperty("items").EnumerateArray().ToList();
        var line = items.Single(i => i.GetProperty("sku").GetString() == Sku);
        Assert.AreEqual(30, line.GetProperty("walmartQuantity").GetInt32());
        Assert.AreEqual(30, line.GetProperty("eshopQuantity").GetInt32());
    }

    [TestMethod]
    public async Task OrderTakesStockOffEshop_VisibleInReport()
    {
        using var factory = new WalmartApiTestFactory();
        var admin = Admin(factory);

        await admin.PutAsJsonAsync($"api/catalog-items/{CatalogItemId}/stock", new { quantity = 10 });

        var order = await Shopper(factory).PostAsJsonAsync("api/orders", new
        {
            items = new[] { new { catalogItemId = CatalogItemId, quantity = 3 } }
        });
        var orderBody = await ReadJson(order);
        Assert.IsTrue(orderBody.GetProperty("orderId").GetInt32() >= 0);

        // eShop level is now 7; sync + report shows it.
        await admin.PostAsync("api/walmart/stock/sync", null);
        var items = (await ReadJson(await admin.GetAsync("api/walmart/stock"))).GetProperty("items").EnumerateArray().ToList();
        var line = items.Single(i => i.GetProperty("sku").GetString() == Sku);
        Assert.AreEqual(7, line.GetProperty("eshopQuantity").GetInt32());
    }

    [TestMethod]
    public async Task OrderBeyondStock_IsRefused()
    {
        using var factory = new WalmartApiTestFactory();
        await Admin(factory).PutAsJsonAsync($"api/catalog-items/{CatalogItemId}/stock", new { quantity = 2 });

        var order = await Shopper(factory).PostAsJsonAsync("api/orders", new
        {
            items = new[] { new { catalogItemId = CatalogItemId, quantity = 5 } }
        });

        Assert.AreEqual(HttpStatusCode.Conflict, order.StatusCode);
    }

    [TestMethod]
    public async Task WalmartEndpointsAreAdminOnly()
    {
        using var factory = new WalmartApiTestFactory();

        var report = await Shopper(factory).GetAsync("api/walmart/stock");
        Assert.AreEqual(HttpStatusCode.Forbidden, report.StatusCode);

        var put = await Shopper(factory).PutAsJsonAsync($"api/walmart/stock/{Sku}", new { quantity = 1 });
        Assert.AreEqual(HttpStatusCode.Forbidden, put.StatusCode);

        var anon = await factory.CreateClient().GetAsync("api/walmart/stock");
        Assert.AreEqual(HttpStatusCode.Unauthorized, anon.StatusCode);
    }

    [TestMethod]
    public async Task OrderIsAvailableToAnySignedInShopper()
    {
        using var factory = new WalmartApiTestFactory();
        await Admin(factory).PutAsJsonAsync($"api/catalog-items/{CatalogItemId}/stock", new { quantity = 4 });

        var order = await Shopper(factory).PostAsJsonAsync("api/orders", new
        {
            items = new[] { new { catalogItemId = CatalogItemId, quantity = 1 } }
        });
        Assert.AreEqual(HttpStatusCode.OK, order.StatusCode);

        var anon = await factory.CreateClient().PostAsJsonAsync("api/orders", new
        {
            items = new[] { new { catalogItemId = CatalogItemId, quantity = 1 } }
        });
        Assert.AreEqual(HttpStatusCode.Unauthorized, anon.StatusCode);
    }
}
