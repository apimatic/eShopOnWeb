using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Walmart;
using Microsoft.eShopWeb.Infrastructure.Walmart;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Walmart;

public class WalmartInventoryServiceTests
{
    private sealed class FakeTokenProvider : IWalmartTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult("test-token");
    }

    private static WalmartInventoryService CreateService(StubHttpMessageHandler handler, string? baseUrl = "http://localhost:39070")
    {
        var httpClient = new HttpClient(handler);
        var options = Options.Create(new WalmartSettings { ClientId = "id", ClientSecret = "secret", BaseUrl = baseUrl });
        return new WalmartInventoryService(httpClient, new FakeTokenProvider(), options, NullLogger<WalmartInventoryService>.Instance);
    }

    [Fact]
    public async Task SetInventoryPutsQuantityAndReturnsEcho()
    {
        var handler = new StubHttpMessageHandler((req, body) =>
            StubHttpMessageHandler.Json(HttpStatusCode.OK, "{\"sellerSku\":\"ESHOP-7\",\"quantity\":25,\"updatedAt\":\"2026-01-01T00:00:00Z\"}"));

        var service = CreateService(handler);
        var result = await service.SetInventoryAsync("ESHOP-7", 25);

        Assert.Equal("ESHOP-7", result.Sku);
        Assert.Equal(25, result.Quantity);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("http://localhost:39070/seller/v1/inventory/v4/items/ESHOP-7", request.Uri.ToString());
        Assert.Equal("Bearer", request.AuthScheme);
        Assert.Equal("test-token", request.AuthParameter);
        Assert.Contains("\"quantity\":25", request.Body);
    }

    [Fact]
    public async Task GetInventoryReadsOnHandQuantity()
    {
        var handler = new StubHttpMessageHandler((req, body) =>
            StubHttpMessageHandler.Json(HttpStatusCode.OK,
                "{\"sellerSku\":\"ESHOP-7\",\"inventoryDetails\":{\"onHandQuantity\":12,\"fulfillableQuantity\":9}}"));

        var service = CreateService(handler);
        var snapshot = await service.GetInventoryAsync("ESHOP-7");

        Assert.NotNull(snapshot);
        Assert.Equal("ESHOP-7", snapshot!.Sku);
        Assert.Equal(12, snapshot.Quantity);
        Assert.Equal(HttpMethod.Get, handler.Requests.Single().Method);
    }

    [Fact]
    public async Task GetInventoryReturnsNullOn404()
    {
        var handler = new StubHttpMessageHandler((req, body) =>
            StubHttpMessageHandler.Json(HttpStatusCode.NotFound, "{\"errors\":[{\"code\":\"NOT_FOUND\",\"message\":\"no such sku\"}]}"));

        var service = CreateService(handler);
        var snapshot = await service.GetInventoryAsync("ESHOP-999");

        Assert.Null(snapshot);
    }

    [Fact]
    public async Task SetInventoryThrowsWalmartApiExceptionOnError()
    {
        var handler = new StubHttpMessageHandler((req, body) =>
            StubHttpMessageHandler.Json(HttpStatusCode.BadRequest, "{\"errors\":[{\"code\":\"INVALID\",\"message\":\"bad quantity\"}]}"));

        var service = CreateService(handler);
        var ex = await Assert.ThrowsAsync<WalmartApiException>(() => service.SetInventoryAsync("ESHOP-7", 5));
        Assert.Contains("INVALID", ex.Message);
    }

    [Fact]
    public async Task SyncMapsSucceededAndErrors()
    {
        var handler = new StubHttpMessageHandler((req, body) =>
            StubHttpMessageHandler.Json(HttpStatusCode.OK,
                "{\"succeeded\":[{\"sellerSku\":\"ESHOP-1\",\"quantity\":5}]," +
                "\"errors\":[{\"sellerSku\":\"ESHOP-2\",\"code\":\"ERR\",\"message\":\"nope\"}]}"));

        var service = CreateService(handler);
        var results = await service.SyncInventoryAsync(new[]
        {
            new WalmartInventoryUpdate("ESHOP-1", 5),
            new WalmartInventoryUpdate("ESHOP-2", 3)
        });

        var r1 = results.Single(r => r.Sku == "ESHOP-1");
        var r2 = results.Single(r => r.Sku == "ESHOP-2");
        Assert.Equal(WalmartSyncStatus.Updated, r1.Status);
        Assert.Equal(WalmartSyncStatus.Failed, r2.Status);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("http://localhost:39070/seller/v1/inventory/v4/items/bulk", request.Uri.ToString());
        Assert.Contains("ESHOP-1", request.Body);
    }

    [Fact]
    public async Task SyncMarksUnreportedSkuAsFailed()
    {
        var handler = new StubHttpMessageHandler((req, body) =>
            StubHttpMessageHandler.Json(HttpStatusCode.OK, "{\"succeeded\":[],\"errors\":[]}"));

        var service = CreateService(handler);
        var results = await service.SyncInventoryAsync(new[] { new WalmartInventoryUpdate("ESHOP-1", 5) });

        Assert.Equal(WalmartSyncStatus.Failed, results.Single().Status);
    }

    [Fact]
    public async Task ListInventoryFollowsPagination()
    {
        var call = 0;
        var handler = new StubHttpMessageHandler((req, body) =>
        {
            call++;
            if (req.RequestUri!.Query.Contains("nextToken"))
            {
                return StubHttpMessageHandler.Json(HttpStatusCode.OK,
                    "{\"inventorySummaries\":[{\"sellerSku\":\"ESHOP-2\",\"inventoryDetails\":{\"onHandQuantity\":2}}]}");
            }
            return StubHttpMessageHandler.Json(HttpStatusCode.OK,
                "{\"pagination\":{\"nextToken\":\"page2\"},\"inventorySummaries\":[{\"sellerSku\":\"ESHOP-1\",\"inventoryDetails\":{\"onHandQuantity\":1}}]}");
        });

        var service = CreateService(handler);
        var all = await service.ListInventoryAsync();

        Assert.Equal(2, all.Count);
        Assert.Equal(1, all.Single(s => s.Sku == "ESHOP-1").Quantity);
        Assert.Equal(2, all.Single(s => s.Sku == "ESHOP-2").Quantity);
        Assert.Equal(2, call); // two pages fetched
    }

    [Fact]
    public async Task RetriesTransientThenSucceeds()
    {
        var call = 0;
        var handler = new StubHttpMessageHandler((req, body) =>
        {
            call++;
            if (call == 1)
            {
                return StubHttpMessageHandler.Json(HttpStatusCode.TooManyRequests, "{\"errors\":[]}");
            }
            return StubHttpMessageHandler.Json(HttpStatusCode.OK, "{\"sellerSku\":\"ESHOP-7\",\"quantity\":1}");
        });

        var service = CreateService(handler);
        var result = await service.SetInventoryAsync("ESHOP-7", 1);

        Assert.Equal(1, result.Quantity);
        Assert.Equal(2, call);
    }
}
