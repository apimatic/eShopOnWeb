using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Marketplace;
using Microsoft.eShopWeb.Infrastructure.Walmart;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Walmart;

public class WalmartInventoryServiceTests
{
    private static WalmartInventoryService CreateService(StubHttpMessageHandler handler)
    {
        var client = new HttpClient(handler)
        {
            // Mirrors the base address the DI wiring would set from the inventory contract server.
            BaseAddress = WalmartEndpoints.ResolveInventoryBase("http://localhost:39190")
        };
        var logger = Substitute.For<IAppLogger<WalmartInventoryService>>();
        return new WalmartInventoryService(client, logger);
    }

    [Fact]
    public async Task SetStock_PutsQuantityToSkuPath()
    {
        var handler = new StubHttpMessageHandler((req, body) =>
            StubHttpMessageHandler.Json(HttpStatusCode.OK, "{\"sellerSku\":\"ESHOP-7\",\"quantity\":5}"));
        var service = CreateService(handler);

        var result = await service.SetStockAsync("ESHOP-7", 5);

        Assert.Equal("ESHOP-7", result.Sku);
        Assert.Equal(5, result.Quantity);

        var req = handler.Requests.Single();
        Assert.Equal(HttpMethod.Put, req.Method);
        Assert.Equal("http://localhost:39190/seller/v1/inventory/v4/items/ESHOP-7", req.RequestUri!.ToString());
        Assert.Contains("\"quantity\":5", handler.RequestBodies.Single());
    }

    [Fact]
    public async Task GetStock_PrefersOnHandQuantity()
    {
        var handler = new StubHttpMessageHandler((req, body) =>
            StubHttpMessageHandler.Json(HttpStatusCode.OK,
                "{\"sellerSku\":\"ESHOP-7\",\"inventoryDetails\":{\"fulfillableQuantity\":4,\"onHandQuantity\":9}}"));
        var service = CreateService(handler);

        var result = await service.GetStockAsync("ESHOP-7");

        Assert.NotNull(result);
        Assert.Equal(9, result!.Quantity);
        Assert.Equal(HttpMethod.Get, handler.Requests.Single().Method);
    }

    [Fact]
    public async Task GetStock_FallsBackToFulfillableWhenNoOnHand()
    {
        var handler = new StubHttpMessageHandler((req, body) =>
            StubHttpMessageHandler.Json(HttpStatusCode.OK,
                "{\"sellerSku\":\"ESHOP-7\",\"inventoryDetails\":{\"fulfillableQuantity\":4}}"));
        var service = CreateService(handler);

        var result = await service.GetStockAsync("ESHOP-7");

        Assert.Equal(4, result!.Quantity);
    }

    [Fact]
    public async Task GetStock_ReturnsNullOn404()
    {
        var handler = new StubHttpMessageHandler((req, body) =>
            StubHttpMessageHandler.Json(HttpStatusCode.NotFound, "{\"errors\":[{\"code\":\"NOT_FOUND\",\"message\":\"no such sku\"}]}"));
        var service = CreateService(handler);

        var result = await service.GetStockAsync("ESHOP-999");

        Assert.Null(result);
    }

    [Fact]
    public async Task SyncStock_MapsSucceededAndErrorsPerSku()
    {
        var handler = new StubHttpMessageHandler((req, body) =>
            StubHttpMessageHandler.Json(HttpStatusCode.OK,
                "{\"succeeded\":[{\"sellerSku\":\"ESHOP-1\",\"quantity\":3},{\"sellerSku\":\"ESHOP-2\",\"quantity\":0}]," +
                "\"errors\":[{\"sellerSku\":\"ESHOP-3\",\"code\":\"X\",\"message\":\"bad\"}]}"));
        var service = CreateService(handler);

        var updates = new List<WalmartInventoryUpdate>
        {
            new("ESHOP-1", 3),
            new("ESHOP-2", 0),
            new("ESHOP-3", 7)
        };

        var result = await service.SyncStockAsync(updates);

        Assert.Equal(3, result.Results.Count);
        Assert.True(result.Results.Single(r => r.Sku == "ESHOP-1").Succeeded);
        Assert.True(result.Results.Single(r => r.Sku == "ESHOP-2").Succeeded);
        Assert.False(result.Results.Single(r => r.Sku == "ESHOP-3").Succeeded);

        var req = handler.Requests.Single();
        Assert.Equal("http://localhost:39190/seller/v1/inventory/v4/items/bulk", req.RequestUri!.ToString());
        var sentBody = handler.RequestBodies.Single();
        Assert.Contains("\"sellerSku\":\"ESHOP-1\"", sentBody);
        Assert.Contains("\"quantity\":7", sentBody);
    }

    [Fact]
    public async Task ListStock_FollowsPaginationToCompletion()
    {
        var page = 0;
        var handler = new StubHttpMessageHandler((req, body) =>
        {
            page++;
            return page == 1
                ? StubHttpMessageHandler.Json(HttpStatusCode.OK,
                    "{\"pagination\":{\"nextToken\":\"TOK2\"},\"inventorySummaries\":[{\"sellerSku\":\"ESHOP-1\",\"inventoryDetails\":{\"onHandQuantity\":2}}]}")
                : StubHttpMessageHandler.Json(HttpStatusCode.OK,
                    "{\"pagination\":{},\"inventorySummaries\":[{\"sellerSku\":\"ESHOP-2\",\"inventoryDetails\":{\"onHandQuantity\":5}}]}");
        });
        var service = CreateService(handler);

        var records = await service.ListStockAsync();

        Assert.Equal(2, records.Count);
        Assert.Equal(2, records.Single(r => r.Sku == "ESHOP-1").Quantity);
        Assert.Equal(5, records.Single(r => r.Sku == "ESHOP-2").Quantity);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("nextToken=TOK2", handler.Requests[1].RequestUri!.ToString());
    }

    [Fact]
    public async Task FailedCall_ThrowsWalmartApiException()
    {
        var handler = new StubHttpMessageHandler((req, body) =>
            StubHttpMessageHandler.Json(HttpStatusCode.InternalServerError, "{\"errors\":[{\"code\":\"E\",\"message\":\"boom\"}]}"));
        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<WalmartApiException>(() => service.SetStockAsync("ESHOP-7", 5));
        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
        Assert.Contains("boom", ex.Message);
    }
}
