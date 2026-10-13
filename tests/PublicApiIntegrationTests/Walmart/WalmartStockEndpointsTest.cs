using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Walmart;

[TestClass]
public class WalmartStockEndpointsTest
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static HttpClient AdminClient(WalmartTestApplicationFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetAdminUserToken());
        return client;
    }

    [TestMethod]
    public async Task SetThenGet_ReturnsWalmartRecordedQuantity()
    {
        using var factory = new WalmartTestApplicationFactory();
        var client = AdminClient(factory);

        var put = await client.PutAsJsonAsync("api/walmart/stock/ESHOP-7", new { quantity = 42 });
        put.EnsureSuccessStatusCode();
        var putBody = await put.Content.ReadFromJsonAsync<StockDto>(JsonOptions);
        Assert.AreEqual("ESHOP-7", putBody!.Sku);
        Assert.AreEqual(42, putBody.Quantity);

        var get = await client.GetAsync("api/walmart/stock/ESHOP-7");
        get.EnsureSuccessStatusCode();
        var getBody = await get.Content.ReadFromJsonAsync<StockDto>(JsonOptions);
        Assert.AreEqual("ESHOP-7", getBody!.Sku);
        Assert.AreEqual(42, getBody.Quantity);
    }

    [TestMethod]
    public async Task Get_UnknownSku_Returns404()
    {
        using var factory = new WalmartTestApplicationFactory();
        var client = AdminClient(factory);

        var get = await client.GetAsync("api/walmart/stock/ESHOP-999999");
        Assert.AreEqual(HttpStatusCode.NotFound, get.StatusCode);
    }

    [TestMethod]
    public async Task OperatorEndpoints_RequireAdminRole()
    {
        using var factory = new WalmartTestApplicationFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var get = await client.GetAsync("api/walmart/stock");
        Assert.AreEqual(HttpStatusCode.Forbidden, get.StatusCode);

        var sync = await client.PostAsync("api/walmart/stock/sync", null);
        Assert.AreEqual(HttpStatusCode.Forbidden, sync.StatusCode);

        var put = await client.PutAsJsonAsync("api/walmart/stock/ESHOP-1", new { quantity = 1 });
        Assert.AreEqual(HttpStatusCode.Forbidden, put.StatusCode);
    }

    [TestMethod]
    public async Task Sync_PushesEveryCatalogItem_UsingEShopLevels()
    {
        using var factory = new WalmartTestApplicationFactory();
        var client = AdminClient(factory);

        // Set one eShop level; the rest default to 0.
        var setStock = await client.PutAsJsonAsync("api/catalog-items/5/stock", new { quantity = 10 });
        setStock.EnsureSuccessStatusCode();

        var sync = await client.PostAsync("api/walmart/stock/sync", null);
        sync.EnsureSuccessStatusCode();
        var syncBody = await sync.Content.ReadFromJsonAsync<SyncResponse>(JsonOptions);

        // One entry per catalog item (12 seeded), all updated.
        Assert.AreEqual(12, syncBody!.Results.Count);
        Assert.IsTrue(syncBody.Results.All(r => r.Status == "updated"));
        var five = syncBody.Results.Single(r => r.Sku == "ESHOP-5");
        Assert.AreEqual("updated", five.Status);

        // Walmart now reflects the pushed levels: ESHOP-5 = 10, a never-set item = 0.
        var getFive = await client.GetAsync("api/walmart/stock/ESHOP-5");
        getFive.EnsureSuccessStatusCode();
        Assert.AreEqual(10, (await getFive.Content.ReadFromJsonAsync<StockDto>(JsonOptions))!.Quantity);

        var getOne = await client.GetAsync("api/walmart/stock/ESHOP-1");
        getOne.EnsureSuccessStatusCode();
        Assert.AreEqual(0, (await getOne.Content.ReadFromJsonAsync<StockDto>(JsonOptions))!.Quantity);
    }

    [TestMethod]
    public async Task Reconcile_LinesUpBothSides_IncludingOneSidedSkus()
    {
        using var factory = new WalmartTestApplicationFactory();
        var client = AdminClient(factory);

        // eShop level 10 for item 5, then push the whole catalog.
        await client.PutAsJsonAsync("api/catalog-items/5/stock", new { quantity = 10 });
        await client.PostAsync("api/walmart/stock/sync", null);

        // A SKU that exists only on Walmart (not an eShop catalog item).
        factory.Walmart.Seed("EXTERNAL-1", 7);

        var report = await client.GetAsync("api/walmart/stock");
        report.EnsureSuccessStatusCode();
        var body = await report.Content.ReadFromJsonAsync<ReportResponse>(JsonOptions);

        var five = body!.Items.Single(i => i.Sku == "ESHOP-5");
        Assert.AreEqual(10, five.WalmartQuantity);
        Assert.AreEqual(10, five.EshopQuantity);

        var external = body.Items.Single(i => i.Sku == "EXTERNAL-1");
        Assert.AreEqual(7, external.WalmartQuantity);
        Assert.IsNull(external.EshopQuantity);
    }

    [TestMethod]
    public async Task Sync_ReportsFailedStatus_WhenWalmartRejectsSku()
    {
        using var factory = new WalmartTestApplicationFactory();
        factory.Walmart.FailBulkForSkus.Add("ESHOP-3");
        var client = AdminClient(factory);

        var sync = await client.PostAsync("api/walmart/stock/sync", null);
        sync.EnsureSuccessStatusCode();
        var body = await sync.Content.ReadFromJsonAsync<SyncResponse>(JsonOptions);

        Assert.AreEqual("failed", body!.Results.Single(r => r.Sku == "ESHOP-3").Status);
        Assert.AreEqual("updated", body.Results.Single(r => r.Sku == "ESHOP-4").Status);
    }

    private sealed class StockDto
    {
        public string Sku { get; set; } = string.Empty;
        public int Quantity { get; set; }
    }

    private sealed class SyncResponse
    {
        public System.Collections.Generic.List<SyncResult> Results { get; set; } = new();
    }

    private sealed class SyncResult
    {
        public string Sku { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }

    private sealed class ReportResponse
    {
        public System.Collections.Generic.List<ReportItem> Items { get; set; } = new();
    }

    private sealed class ReportItem
    {
        public string Sku { get; set; } = string.Empty;
        public int? WalmartQuantity { get; set; }
        public int? EshopQuantity { get; set; }
    }
}
