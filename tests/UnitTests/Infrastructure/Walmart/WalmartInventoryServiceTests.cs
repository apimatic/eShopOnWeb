using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Marketplace;
using Microsoft.eShopWeb.Infrastructure.Marketplace.Walmart;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Walmart;

public class WalmartInventoryServiceTests
{
    private static WalmartInventoryService CreateService(StubHttpMessageHandler handler, string? baseUrl = null)
    {
        var settings = Options.Create(new WalmartSettings { ClientId = "id", ClientSecret = "secret", BaseUrl = baseUrl });
        return new WalmartInventoryService(
            new SingleClientFactory(handler),
            new FixedTokenProvider(),
            settings,
            NullLogger<WalmartInventoryService>.Instance);
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    [Fact]
    public async Task SetStock_PutsToSkuPath_WithBearer_AndReturnsEchoedQuantity()
    {
        var handler = new StubHttpMessageHandler(_ =>
            Json("{\"sellerSku\":\"ESHOP-7\",\"quantity\":42,\"updatedAt\":\"2026-01-01T00:00:00Z\"}"));
        var service = CreateService(handler, baseUrl: "http://localhost:9000");

        var result = await service.SetStockAsync("ESHOP-7", 42);

        var request = handler.Requests.Single();
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("http://localhost:9000/seller/v1/inventory/v4/items/ESHOP-7", request.RequestUri!.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("test-access-token", request.Headers.Authorization!.Parameter);
        Assert.Contains("\"quantity\":42", handler.RequestBodies.Single());

        Assert.Equal("ESHOP-7", result.Sku);
        Assert.Equal(42, result.Quantity);
    }

    [Fact]
    public async Task GetStock_ReadsOnHandQuantity()
    {
        var handler = new StubHttpMessageHandler(_ =>
            Json("{\"sellerSku\":\"ESHOP-7\",\"inventoryDetails\":{\"onHandQuantity\":15,\"fulfillableQuantity\":9}}"));
        var service = CreateService(handler);

        var result = await service.GetStockAsync("ESHOP-7");

        Assert.Equal(HttpMethod.Get, handler.Requests.Single().Method);
        Assert.Equal("ESHOP-7", result.Sku);
        Assert.Equal(15, result.Quantity);
    }

    [Fact]
    public async Task GetStock_FallsBackToFulfillableWhenOnHandMissing()
    {
        var handler = new StubHttpMessageHandler(_ =>
            Json("{\"sellerSku\":\"ESHOP-7\",\"inventoryDetails\":{\"fulfillableQuantity\":9}}"));
        var service = CreateService(handler);

        var result = await service.GetStockAsync("ESHOP-7");

        Assert.Equal(9, result.Quantity);
    }

    [Fact]
    public async Task Sync_PostsBulk_AndMapsSucceededAndErrors()
    {
        var handler = new StubHttpMessageHandler(_ => Json(
            "{\"succeeded\":[{\"sellerSku\":\"ESHOP-1\",\"quantity\":5}]," +
            "\"errors\":[{\"sellerSku\":\"ESHOP-2\",\"code\":\"BAD\",\"message\":\"nope\"}]}"));
        var service = CreateService(handler, baseUrl: "http://localhost:9000");

        var items = new List<WalmartSkuStock>
        {
            new("ESHOP-1", 5),
            new("ESHOP-2", 9),
        };

        var results = await service.SyncStockAsync(items);

        var request = handler.Requests.Single();
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("http://localhost:9000/seller/v1/inventory/v4/items/bulk", request.RequestUri!.ToString());

        var ok = results.Single(r => r.Sku == "ESHOP-1");
        Assert.True(ok.Success);
        var failed = results.Single(r => r.Sku == "ESHOP-2");
        Assert.False(failed.Success);
        Assert.Equal("BAD", failed.ErrorCode);
    }

    [Fact]
    public async Task Sync_WholeBatchHttpFailure_MarksEverySkuFailed()
    {
        var handler = new StubHttpMessageHandler(_ => Json("{\"errors\":[]}", HttpStatusCode.InternalServerError));
        var service = CreateService(handler);

        var items = new List<WalmartSkuStock> { new("ESHOP-1", 5), new("ESHOP-2", 9) };

        var results = await service.SyncStockAsync(items);

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.False(r.Success));
    }

    [Fact]
    public async Task ListStock_PagesThroughNextToken()
    {
        var call = 0;
        var handler = new StubHttpMessageHandler(_ =>
        {
            call++;
            return call == 1
                ? Json("{\"pagination\":{\"nextToken\":\"page2\"},\"inventorySummaries\":[{\"sellerSku\":\"ESHOP-1\",\"inventoryDetails\":{\"onHandQuantity\":1}}]}")
                : Json("{\"pagination\":{},\"inventorySummaries\":[{\"sellerSku\":\"ESHOP-2\",\"inventoryDetails\":{\"onHandQuantity\":2}}]}");
        });
        var service = CreateService(handler);

        var all = await service.ListStockAsync();

        Assert.Equal(2, handler.Requests.Count);
        // Second request carries the nextToken from the first page.
        Assert.Contains("nextToken=page2", handler.Requests[1].RequestUri!.Query);
        Assert.Equal(2, all.Count);
        Assert.Equal(1, all.Single(s => s.Sku == "ESHOP-1").Quantity);
        Assert.Equal(2, all.Single(s => s.Sku == "ESHOP-2").Quantity);
    }

    [Fact]
    public async Task NonSuccessStatus_ThrowsWalmartApiException()
    {
        var handler = new StubHttpMessageHandler(_ =>
            Json("{\"errors\":[{\"code\":\"NOT_FOUND\",\"message\":\"missing\"}]}", HttpStatusCode.NotFound));
        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<WalmartApiException>(() => service.GetStockAsync("ESHOP-99"));
        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
    }
}
