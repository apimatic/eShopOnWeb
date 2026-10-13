using System.Collections.Generic;
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
public class WalmartStockEndpointsTests
{
    private static WalmartTestWebApplicationFactory _factory = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext _) => _factory = new WalmartTestWebApplicationFactory();

    [ClassCleanup]
    public static void ClassCleanup() => _factory.Dispose();

    [TestInitialize]
    public void TestInitialize() => _factory.ResetWalmart();

    private HttpClient Anonymous() => _factory.CreateClient();

    private HttpClient AsAdmin()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetAdminUserToken());
        return client;
    }

    private HttpClient AsShopper()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        return client;
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private async Task<int> CreateCatalogItemAsync()
    {
        var client = AsAdmin();
        var response = await client.PostAsJsonAsync("api/catalog-items", new
        {
            catalogBrandId = 1,
            catalogTypeId = 2,
            description = "walmart test item",
            name = "walmart test item " + System.Guid.NewGuid(),
            price = 9.99m,
        });
        response.EnsureSuccessStatusCode();
        var json = await ReadJson(response);
        return json.GetProperty("catalogItem").GetProperty("id").GetInt32();
    }

    private async Task SetEshopStockAsync(int catalogItemId, int quantity)
    {
        var client = AsAdmin();
        var response = await client.PutAsJsonAsync($"api/catalog-items/{catalogItemId}/stock", new { quantity });
        response.EnsureSuccessStatusCode();
    }

    // ---- Authorization ----

    [TestMethod]
    public async Task StockReport_RequiresAuthentication()
    {
        var response = await Anonymous().GetAsync("api/walmart/stock");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task StockReport_ForbidsNonAdmin()
    {
        var response = await AsShopper().GetAsync("api/walmart/stock");
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task SetStock_ForbidsNonAdmin()
    {
        var response = await AsShopper().PutAsJsonAsync("api/walmart/stock/ESHOP-1", new { quantity = 1 });
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- Flow 1: one SKU ----

    [TestMethod]
    public async Task SetThenGet_SingleSku_RoundTripsThroughWalmart()
    {
        const string sku = "ESHOP-55501";

        var setResponse = await AsAdmin().PutAsJsonAsync($"api/walmart/stock/{sku}", new { quantity = 17 });
        setResponse.EnsureSuccessStatusCode();
        var set = await ReadJson(setResponse);
        Assert.AreEqual(sku, set.GetProperty("sku").GetString());
        Assert.AreEqual(17, set.GetProperty("quantity").GetInt32());

        // GET reads Walmart's own record (what the fake now holds), not a local copy.
        Assert.AreEqual(17, _factory.Handler.OnHand[sku]);

        var getResponse = await AsAdmin().GetAsync($"api/walmart/stock/{sku}");
        getResponse.EnsureSuccessStatusCode();
        var get = await ReadJson(getResponse);
        Assert.AreEqual(sku, get.GetProperty("sku").GetString());
        Assert.AreEqual(17, get.GetProperty("quantity").GetInt32());
    }

    [TestMethod]
    public async Task SetStock_RejectsNegativeQuantity()
    {
        var response = await AsAdmin().PutAsJsonAsync("api/walmart/stock/ESHOP-1", new { quantity = -1 });
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- Flow 2: whole catalog sync ----

    [TestMethod]
    public async Task Sync_PushesEveryCatalogItemsEshopLevelToWalmart()
    {
        var id = await CreateCatalogItemAsync();
        await SetEshopStockAsync(id, 23);
        var sku = $"ESHOP-{id}";

        var response = await AsAdmin().PostAsync("api/walmart/stock/sync", content: null);
        response.EnsureSuccessStatusCode();
        var json = await ReadJson(response);

        var entry = json.GetProperty("results").EnumerateArray()
            .Single(e => e.GetProperty("sku").GetString() == sku);
        Assert.AreEqual("updated", entry.GetProperty("status").GetString());
        Assert.AreEqual(23, _factory.Handler.OnHand[sku]);
    }

    [TestMethod]
    public async Task Sync_ReportsPerSkuFailure_AndKeepsGoing()
    {
        var goodId = await CreateCatalogItemAsync();
        var badId = await CreateCatalogItemAsync();
        await SetEshopStockAsync(goodId, 3);
        await SetEshopStockAsync(badId, 4);
        var badSku = $"ESHOP-{badId}";
        _factory.Handler.FailUpdateForSku = s => s == badSku;

        var response = await AsAdmin().PostAsync("api/walmart/stock/sync", content: null);
        response.EnsureSuccessStatusCode();
        var results = (await ReadJson(response)).GetProperty("results").EnumerateArray().ToList();

        Assert.AreEqual("updated", results.Single(e => e.GetProperty("sku").GetString() == $"ESHOP-{goodId}").GetProperty("status").GetString());
        Assert.AreEqual("failed", results.Single(e => e.GetProperty("sku").GetString() == badSku).GetProperty("status").GetString());
    }

    // ---- Flow 3: comparison report ----

    [TestMethod]
    public async Task Report_ShowsAgreementDivergenceAndOneSidedSkus()
    {
        var id = await CreateCatalogItemAsync();
        await SetEshopStockAsync(id, 10);
        var eshopSku = $"ESHOP-{id}";

        // Agreement: sync pushes eShop's level to Walmart.
        (await AsAdmin().PostAsync("api/walmart/stock/sync", content: null)).EnsureSuccessStatusCode();

        // Divergence: change Walmart's record for this SKU directly.
        await AsAdmin().PutAsJsonAsync($"api/walmart/stock/{eshopSku}", new { quantity = 99 });

        // Walmart-only SKU (no matching catalog item).
        await AsAdmin().PutAsJsonAsync("api/walmart/stock/ACME-ONLY", new { quantity = 5 });

        var response = await AsAdmin().GetAsync("api/walmart/stock");
        response.EnsureSuccessStatusCode();
        var json = await ReadJson(response);
        var items = json.GetProperty("items").EnumerateArray().ToList();

        var diverged = items.Single(e => e.GetProperty("sku").GetString() == eshopSku);
        Assert.AreEqual(99, diverged.GetProperty("walmartQuantity").GetInt32());
        Assert.AreEqual(10, diverged.GetProperty("eshopQuantity").GetInt32());

        var walmartOnly = items.Single(e => e.GetProperty("sku").GetString() == "ACME-ONLY");
        Assert.AreEqual(5, walmartOnly.GetProperty("walmartQuantity").GetInt32());
        Assert.AreEqual(JsonValueKind.Null, walmartOnly.GetProperty("eshopQuantity").ValueKind);
    }

    [TestMethod]
    public async Task Report_ShowsEshopOnlySku_WhenNotOnWalmart()
    {
        var id = await CreateCatalogItemAsync();
        await SetEshopStockAsync(id, 8);
        var eshopSku = $"ESHOP-{id}";
        // No sync, Walmart holds nothing for this SKU.

        var response = await AsAdmin().GetAsync("api/walmart/stock");
        response.EnsureSuccessStatusCode();
        var items = (await ReadJson(response)).GetProperty("items").EnumerateArray().ToList();

        var eshopOnly = items.Single(e => e.GetProperty("sku").GetString() == eshopSku);
        Assert.AreEqual(JsonValueKind.Null, eshopOnly.GetProperty("walmartQuantity").ValueKind);
        Assert.AreEqual(8, eshopOnly.GetProperty("eshopQuantity").GetInt32());
    }

    // ---- Unknown-outcome reconciliation ----

    [TestMethod]
    public async Task SetStock_TransportFailureButWriteLanded_SettlesAsSuccess()
    {
        const string sku = "ESHOP-55502";
        _factory.Handler.OnHand[sku] = 12;               // the write "landed" despite the connection error
        _factory.Handler.FailAllUpdatesWithTransportError = true;

        var response = await AsAdmin().PutAsJsonAsync($"api/walmart/stock/{sku}", new { quantity = 12 });

        response.EnsureSuccessStatusCode();
        var json = await ReadJson(response);
        Assert.AreEqual(12, json.GetProperty("quantity").GetInt32());
    }

    [TestMethod]
    public async Task SetStock_TransportFailureAndWriteDidNotLand_SurfacesAsBadGateway()
    {
        const string sku = "ESHOP-55503";
        _factory.Handler.OnHand[sku] = 3;                // Walmart holds something other than the intended 12
        _factory.Handler.FailAllUpdatesWithTransportError = true;

        var response = await AsAdmin().PutAsJsonAsync($"api/walmart/stock/{sku}", new { quantity = 12 });

        Assert.AreEqual(HttpStatusCode.BadGateway, response.StatusCode);
    }
}
