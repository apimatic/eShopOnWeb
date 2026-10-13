using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.WalmartEndpoints;

/// <summary>
/// Exercises the Walmart stock endpoints with a fake Walmart service injected, proving the
/// endpoint wiring, response shapes and the reconciliation report without a live Walmart.
/// </summary>
[TestClass]
public class WalmartStockEndpointTest
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private static (HttpClient client, FakeWalmartInventoryService fake) AdminClientWithFake()
    {
        var fake = new FakeWalmartInventoryService();
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IWalmartInventoryService>();
                services.AddScoped<IWalmartInventoryService>(_ => fake);
            });
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetAdminUserToken());
        return (client, fake);
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    [TestMethod]
    public async Task PutStockReturnsSkuAndQuantity()
    {
        var (client, fake) = AdminClientWithFake();

        var response = await client.PutAsync("api/walmart/stock/ESHOP-5", Json("{\"quantity\":33}"));
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual("ESHOP-5", doc.RootElement.GetProperty("sku").GetString());
        Assert.AreEqual(33, doc.RootElement.GetProperty("quantity").GetInt32());
        Assert.AreEqual(1, fake.SetCalls.Count);
        Assert.AreEqual(33, fake.SetCalls.Single().Quantity);
    }

    [TestMethod]
    public async Task GetStockReturnsWalmartRecordedQuantity()
    {
        var (client, _) = AdminClientWithFake();

        var response = await client.GetAsync("api/walmart/stock/ESHOP-5");
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual("ESHOP-5", doc.RootElement.GetProperty("sku").GetString());
        Assert.AreEqual(99, doc.RootElement.GetProperty("quantity").GetInt32());
    }

    [TestMethod]
    public async Task PutStockRejectsNegativeQuantity()
    {
        var (client, _) = AdminClientWithFake();
        var response = await client.PutAsync("api/walmart/stock/ESHOP-5", Json("{\"quantity\":-1}"));
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task SyncPushesEveryCatalogItemAndReportsPerSkuStatus()
    {
        var (client, fake) = AdminClientWithFake();

        var response = await client.PostAsync("api/walmart/stock/sync", Json("{}"));
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var results = doc.RootElement.GetProperty("results");

        // One result per seeded catalog item (12), each with a status.
        Assert.AreEqual(12, results.GetArrayLength());
        Assert.IsTrue(fake.LastSyncItems.Count >= 12);
        Assert.IsTrue(fake.LastSyncItems.All(i => i.Sku.StartsWith("ESHOP-")));

        // ESHOP-1 was simulated as a failure; others updated.
        var statuses = results.EnumerateArray()
            .ToDictionary(e => e.GetProperty("sku").GetString()!, e => e.GetProperty("status").GetString());
        Assert.AreEqual("failed", statuses["ESHOP-1"]);
        Assert.AreEqual("updated", statuses["ESHOP-2"]);
    }

    [TestMethod]
    public async Task ReportLinesUpWalmartAndEShopIncludingOneSidedSkus()
    {
        var (client, _) = AdminClientWithFake();

        var response = await client.GetAsync("api/walmart/stock");
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = doc.RootElement.GetProperty("items").EnumerateArray()
            .ToDictionary(
                e => e.GetProperty("sku").GetString()!,
                e => (
                    walmart: e.GetProperty("walmartQuantity"),
                    eshop: e.GetProperty("eshopQuantity")));

        // ESHOP-1 exists on both sides.
        Assert.IsTrue(items.ContainsKey("ESHOP-1"));
        Assert.AreEqual(100, items["ESHOP-1"].walmart.GetInt32());
        Assert.AreEqual(JsonValueKind.Number, items["ESHOP-1"].eshop.ValueKind);

        // WALMART-ONLY exists only on Walmart: eshopQuantity is null.
        Assert.IsTrue(items.ContainsKey("WALMART-ONLY"));
        Assert.AreEqual(7, items["WALMART-ONLY"].walmart.GetInt32());
        Assert.AreEqual(JsonValueKind.Null, items["WALMART-ONLY"].eshop.ValueKind);

        // An eShop catalog SKU that Walmart does not hold: walmartQuantity is null.
        Assert.IsTrue(items.ContainsKey("ESHOP-5"));
        Assert.AreEqual(JsonValueKind.Null, items["ESHOP-5"].walmart.ValueKind);
        Assert.AreEqual(JsonValueKind.Number, items["ESHOP-5"].eshop.ValueKind);
    }
}
