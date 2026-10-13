using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Marketplace;
using Microsoft.eShopWeb.Infrastructure.Marketplace;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Marketplace;

public class WalmartInventoryServiceTests
{
    private static WalmartInventoryService Service(StubHandler handler, string? baseUrl = null) =>
        new(WalmartClientFactory.Create(handler, baseUrl), new NullAppLogger<WalmartInventoryService>());

    [Fact]
    public async Task SetStock_SendsQuantityAndReturnsIt()
    {
        var handler = new StubHandler(StubHandler.Json(HttpStatusCode.OK,
            """{"sellerSku":"ESHOP-7","quantity":20}"""));
        var service = Service(handler);

        var result = await service.SetStockAsync("ESHOP-7", 20);

        Assert.Equal("ESHOP-7", result.Sku);
        Assert.Equal(20, result.Quantity);
        // The PUT carried the quantity and targeted the SKU's inventory route.
        Assert.Contains("ESHOP-7", handler.RequestUris[0].AbsolutePath);
        Assert.Contains("\"quantity\":20", handler.Bodies[0]);
    }

    [Fact]
    public async Task GetStock_ReadsOnHandQuantity()
    {
        var handler = new StubHandler(StubHandler.Json(HttpStatusCode.OK,
            """{"sellerSku":"ESHOP-7","inventoryDetails":{"onHandQuantity":42,"fulfillableQuantity":40}}"""));

        var result = await Service(handler).GetStockAsync("ESHOP-7");

        Assert.Equal(42, result.Quantity);
    }

    [Fact]
    public async Task GetStock_FallsBackToFulfillableThenZero()
    {
        var handler = new StubHandler(StubHandler.Json(HttpStatusCode.OK,
            """{"sellerSku":"ESHOP-7","inventoryDetails":{"fulfillableQuantity":5}}"""));
        Assert.Equal(5, (await Service(handler).GetStockAsync("ESHOP-7")).Quantity);

        var empty = new StubHandler(StubHandler.Json(HttpStatusCode.OK, """{"sellerSku":"ESHOP-7"}"""));
        Assert.Equal(0, (await Service(empty).GetStockAsync("ESHOP-7")).Quantity);
    }

    [Fact]
    public async Task Sync_MarksErroredSkusFailedAndRestUpdated()
    {
        var handler = new StubHandler(StubHandler.Json(HttpStatusCode.OK,
            """{"succeeded":[{"sellerSku":"ESHOP-1"}],"errors":[{"sellerSku":"ESHOP-2","code":"BAD","message":"nope"}]}"""));

        var results = await Service(handler).SyncAsync(new[] { (1, 10), (2, 5) });

        Assert.Equal(WalmartSyncStatus.Updated, results.Single(r => r.Sku == "ESHOP-1").Status);
        Assert.Equal(WalmartSyncStatus.Failed, results.Single(r => r.Sku == "ESHOP-2").Status);
    }

    [Fact]
    public async Task ListAllStock_WalksEveryPage()
    {
        var handler = new StubHandler(
            StubHandler.Json(HttpStatusCode.OK,
                """{"pagination":{"nextToken":"t1"},"inventorySummaries":[{"sellerSku":"ESHOP-1","inventoryDetails":{"onHandQuantity":5}}]}"""),
            StubHandler.Json(HttpStatusCode.OK,
                """{"inventorySummaries":[{"sellerSku":"ESHOP-9","inventoryDetails":{"onHandQuantity":3}}]}"""));

        var all = await Service(handler).ListAllStockAsync();

        Assert.Equal(2, all.Count);
        Assert.Equal(5, all.Single(s => s.Sku == "ESHOP-1").Quantity);
        Assert.Equal(3, all.Single(s => s.Sku == "ESHOP-9").Quantity);
    }

    [Fact]
    public async Task ApiError_TranslatesToWalmartInventoryExceptionWithStatus()
    {
        var handler = new StubHandler(StubHandler.Json(HttpStatusCode.BadRequest,
            """{"errors":[{"code":"400","description":"bad"}]}"""));

        var ex = await Assert.ThrowsAsync<WalmartInventoryException>(() =>
            Service(handler).GetStockAsync("ESHOP-7"));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }

    [Fact]
    public async Task ConnectionFailure_TranslatesWithNoStatus()
    {
        var handler = new StubHandler(StubHandler.Throw());

        var ex = await Assert.ThrowsAsync<WalmartInventoryException>(() =>
            Service(handler).GetStockAsync("ESHOP-7"));

        Assert.Null(ex.StatusCode);
    }

    [Fact]
    public async Task BaseUrlOverride_RedirectsEveryCallIncludingPathPreservation()
    {
        var handler = new StubHandler(StubHandler.Json(HttpStatusCode.OK,
            """{"sellerSku":"ESHOP-7","inventoryDetails":{"onHandQuantity":1}}"""));

        await Service(handler, "http://localhost:9999").GetStockAsync("ESHOP-7");

        var opUri = handler.RequestUris[0];
        Assert.Equal("localhost", opUri.Host);
        Assert.Equal(9999, opUri.Port);
        // The contract's base path is preserved after the swapped host.
        Assert.StartsWith("/seller/v1", opUri.AbsolutePath);

        // Token acquisition is redirected too, preserving its path.
        var tokenUri = Assert.Single(handler.TokenRequestUris);
        Assert.Equal("localhost", tokenUri.Host);
        Assert.Equal(9999, tokenUri.Port);
        Assert.Equal("/seller/v1/auth/token", tokenUri.AbsolutePath);
    }
}
