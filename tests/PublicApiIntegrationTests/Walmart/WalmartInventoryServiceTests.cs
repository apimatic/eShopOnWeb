using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Walmart;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Walmart;

/// <summary>
/// Verifies the Walmart integration against the real SDK client over a stubbed transport — the
/// SDK wire interaction, quantity read, sync result mapping, pagination and error translation —
/// with no network.
/// </summary>
[TestClass]
public class WalmartInventoryServiceTests
{
    private static WalmartInventoryService Service(StubHandler handler) =>
        new(WalmartTestClient.Create(handler), NullLogger<WalmartInventoryService>.Instance);

    [TestMethod]
    public async Task SetStock_PutsQuantity_AndReturnsEchoedLevel()
    {
        var handler = new StubHandler(_ => (HttpStatusCode.OK,
            """{ "sellerSku": "ESHOP-7", "quantity": 25 }"""));
        var sut = Service(handler);

        var level = await sut.SetStockAsync("ESHOP-7", 25, CancellationToken.None);

        Assert.AreEqual("ESHOP-7", level.Sku);
        Assert.AreEqual(25, level.Quantity);
        var put = handler.Requests.Single(r => r.Method == HttpMethod.Put);
        StringAssert.Contains(put.RequestUri!.AbsolutePath, "/inventory/v4/items/ESHOP-7");
        var body = handler.Bodies[handler.Requests.IndexOf(put)];
        StringAssert.Contains(body, "\"quantity\":25");
    }

    [TestMethod]
    public async Task GetStock_ReadsOnHandQuantityFromSummary()
    {
        var handler = new StubHandler(_ => (HttpStatusCode.OK,
            """{ "sellerSku": "ESHOP-7", "inventoryDetails": { "onHandQuantity": 12, "fulfillableQuantity": 9 } }"""));
        var sut = Service(handler);

        var level = await sut.GetStockAsync("ESHOP-7", CancellationToken.None);

        Assert.AreEqual("ESHOP-7", level.Sku);
        Assert.AreEqual(12, level.Quantity); // prefers on-hand
    }

    [TestMethod]
    public async Task GetStock_FallsBackToFulfillableWhenNoOnHand()
    {
        var handler = new StubHandler(_ => (HttpStatusCode.OK,
            """{ "sellerSku": "ESHOP-7", "inventoryDetails": { "fulfillableQuantity": 9 } }"""));
        var sut = Service(handler);

        var level = await sut.GetStockAsync("ESHOP-7", CancellationToken.None);

        Assert.AreEqual(9, level.Quantity);
    }

    [TestMethod]
    public async Task Sync_MapsSucceededAndFailedPerSku()
    {
        var handler = new StubHandler(_ => (HttpStatusCode.OK,
            """{ "succeeded": [ { "sellerSku": "ESHOP-1" } ], "errors": [ { "sellerSku": "ESHOP-2", "message": "bad" } ] }"""));
        var sut = Service(handler);

        var results = await sut.SyncAsync(new[]
        {
            new WalmartSkuQuantity("ESHOP-1", 5),
            new WalmartSkuQuantity("ESHOP-2", 0),
        }, CancellationToken.None);

        Assert.AreEqual("updated", results.Single(r => r.Sku == "ESHOP-1").Status);
        Assert.AreEqual("failed", results.Single(r => r.Sku == "ESHOP-2").Status);
    }

    [TestMethod]
    public async Task ListAll_WalksAllPagesViaNextToken()
    {
        var page = 0;
        var handler = new StubHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/inventory/v4/items"))
            {
                page++;
                return page == 1
                    ? (HttpStatusCode.OK, """{ "inventorySummaries": [ { "sellerSku": "ESHOP-1", "inventoryDetails": { "onHandQuantity": 3 } } ], "pagination": { "nextToken": "T2" } }""")
                    : (HttpStatusCode.OK, """{ "inventorySummaries": [ { "sellerSku": "ESHOP-2", "inventoryDetails": { "onHandQuantity": 8 } } ], "pagination": { } }""");
            }
            return (HttpStatusCode.OK, "{}");
        });
        var sut = Service(handler);

        var listing = await sut.ListAllAsync(CancellationToken.None);

        Assert.IsFalse(listing.Truncated);
        Assert.AreEqual(2, listing.Items.Count);
        Assert.AreEqual(3, listing.Items.Single(i => i.Sku == "ESHOP-1").Quantity);
        Assert.AreEqual(8, listing.Items.Single(i => i.Sku == "ESHOP-2").Quantity);
    }

    [TestMethod]
    public async Task GetStock_TranslatesTypedErrorToIntegrationException()
    {
        var handler = new StubHandler(_ => (HttpStatusCode.NotFound,
            """{ "errors": [ { "code": "NOT_FOUND", "message": "no such sku" } ] }"""));
        var sut = Service(handler);

        var ex = await Assert.ThrowsExceptionAsync<WalmartIntegrationException>(
            () => sut.GetStockAsync("ESHOP-999", CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.NotFound, ex.StatusCode);
        StringAssert.Contains(ex.Message, "NOT_FOUND");
    }

    [TestMethod]
    public async Task SetStock_TranslatesConnectionFaultToIntegrationException_WithNoStatus()
    {
        // The transport throws: the write's outcome is unknown. The boundary surfaces it as an
        // integration failure with no status (not a false success) — an idempotent re-set/re-sync
        // then settles it.
        var handler = new StubHandler(_ => throw new HttpRequestException("connection reset"));
        var sut = Service(handler);

        var ex = await Assert.ThrowsExceptionAsync<WalmartIntegrationException>(
            () => sut.SetStockAsync("ESHOP-7", 5, CancellationToken.None));

        Assert.IsNull(ex.StatusCode);
    }
}
