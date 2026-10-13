using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.WalmartIntegration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WalmartApIs;
using WalmartApIs.Core.Authentication.OAuth2.ClientCredentials;
using WalmartApIs.Core.Exceptions;

namespace PublicApiIntegrationTests.WalmartIntegration;

[TestClass]
public class WalmartInventoryServiceTests
{
    private const string TokenJson = """{ "access_token": "t", "token_type": "Bearer", "expires_in": 3600 }""";

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Json)> _opResponses;
        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string?> Bodies { get; } = new();

        public StubHandler(Queue<(HttpStatusCode, string)> opResponses) => _opResponses = opResponses;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? null : request.Content.ReadAsStringAsync().Result);

            HttpResponseMessage response;
            if (request.RequestUri!.AbsolutePath.EndsWith("/auth/token", StringComparison.Ordinal))
            {
                response = Json(HttpStatusCode.OK, TokenJson);
            }
            else
            {
                var (status, json) = _opResponses.Dequeue();
                response = Json(status, json);
            }
            response.RequestMessage = request;
            return Task.FromResult(response);
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
            new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private static (WalmartInventoryService Service, StubHandler Handler) Build(
        Queue<(HttpStatusCode, string)> opResponses, IReadRepository<CatalogItem>? catalog = null)
    {
        var handler = new StubHandler(opResponses);
        var options = new WalmartApIsClientOptions
        {
            WalletAuth = new OAuth2ClientCredentials { ClientId = "id", ClientSecret = "secret" },
        };
        var client = new WalmartApIsClient(new HttpClient(handler), options);
        var service = new WalmartInventoryService(
            client, catalog ?? EmptyCatalog(), NullLogger<WalmartInventoryService>.Instance);
        return (service, handler);
    }

    private static CatalogContext NewContext() =>
        new(new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase("walmart-tests-" + Guid.NewGuid()).Options);

    private static IReadRepository<CatalogItem> EmptyCatalog() => new EfRepository<CatalogItem>(NewContext());

    private static Queue<(HttpStatusCode, string)> Q(params (HttpStatusCode, string)[] items) => new(items);

    [TestMethod]
    public async Task GetStockAsync_ReturnsFulfillableQuantity()
    {
        var (service, _) = Build(Q((HttpStatusCode.OK,
            """{ "sellerSku": "ESHOP-7", "inventoryDetails": { "fulfillableQuantity": 42, "onHandQuantity": 50 } }""")));

        var quantity = await service.GetStockAsync("ESHOP-7", CancellationToken.None);

        Assert.AreEqual(42, quantity);
    }

    [TestMethod]
    public async Task GetStockAsync_FallsBackToOnHandQuantity()
    {
        var (service, _) = Build(Q((HttpStatusCode.OK,
            """{ "sellerSku": "ESHOP-7", "inventoryDetails": { "onHandQuantity": 9 } }""")));

        var quantity = await service.GetStockAsync("ESHOP-7", CancellationToken.None);

        Assert.AreEqual(9, quantity);
    }

    [TestMethod]
    public async Task GetStockAsync_ThrowsWalmartApiExceptionOnNotFound()
    {
        var (service, _) = Build(Q((HttpStatusCode.NotFound,
            """{ "errors": [ { "code": "NOT_FOUND", "message": "no such sku" } ] }""")));

        var ex = await Assert.ThrowsExceptionAsync<WalmartApiException>(
            () => service.GetStockAsync("ESHOP-7", CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.NotFound, ex.StatusCode);
    }

    [TestMethod]
    public async Task SetStockAsync_SendsQuantityAndSkuInRoute()
    {
        var (service, handler) = Build(Q((HttpStatusCode.OK,
            """{ "sellerSku": "ESHOP-7", "quantity": 5 }""")));

        await service.SetStockAsync("ESHOP-7", 5, CancellationToken.None);

        var opRequest = handler.Requests.First(r => !r.RequestUri!.AbsolutePath.EndsWith("/auth/token", StringComparison.Ordinal));
        Assert.AreEqual(HttpMethod.Put, opRequest.Method);
        StringAssert.Contains(opRequest.RequestUri!.AbsolutePath, "ESHOP-7");
        var body = handler.Bodies[handler.Requests.IndexOf(opRequest)];
        StringAssert.Contains(body, "\"quantity\":5");
    }

    [TestMethod]
    public async Task SyncCatalogAsync_ReportsUpdatedAndFailedPerSku()
    {
        using var context = NewContext();
        var repo = new EfRepository<CatalogItem>(context);
        var itemA = await repo.AddAsync(new CatalogItem(1, 1, "a", "A", 1m, "a.png"));
        var itemB = await repo.AddAsync(new CatalogItem(1, 1, "b", "B", 2m, "b.png"));
        itemA.SetStock(3);
        itemB.SetStock(4);
        await repo.UpdateAsync(itemA);
        await repo.UpdateAsync(itemB);

        var skuB = WalmartSku.ForCatalogItem(itemB.Id);
        var (service, _) = Build(
            Q((HttpStatusCode.OK, $$"""{ "succeeded": [], "errors": [ { "sellerSku": "{{skuB}}", "code": "X", "message": "bad" } ] }""")),
            repo);

        var results = await service.SyncCatalogAsync(CancellationToken.None);

        Assert.AreEqual(2, results.Count);
        Assert.AreEqual(SkuSyncResult.Updated, results.Single(r => r.Sku == WalmartSku.ForCatalogItem(itemA.Id)).Status);
        Assert.AreEqual(SkuSyncResult.Failed, results.Single(r => r.Sku == skuB).Status);
    }

    [TestMethod]
    public async Task SyncCatalogAsync_MarksAllFailedWhenBulkCallFails()
    {
        using var context = NewContext();
        var repo = new EfRepository<CatalogItem>(context);
        var item = await repo.AddAsync(new CatalogItem(1, 1, "a", "A", 1m, "a.png"));

        var (service, _) = Build(Q((HttpStatusCode.InternalServerError,
            """{ "errors": [ { "code": "ERR", "message": "boom" } ] }""")), repo);

        var results = await service.SyncCatalogAsync(CancellationToken.None);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual(SkuSyncResult.Failed, results.Single(r => r.Sku == WalmartSku.ForCatalogItem(item.Id)).Status);
    }

    [TestMethod]
    public async Task ListWalmartStockAsync_WalksAllPages()
    {
        var (service, _) = Build(Q(
            (HttpStatusCode.OK, """{ "pagination": { "nextToken": "p2" }, "inventorySummaries": [ { "sellerSku": "ESHOP-1", "inventoryDetails": { "fulfillableQuantity": 5 } } ] }"""),
            (HttpStatusCode.OK, """{ "pagination": {}, "inventorySummaries": [ { "sellerSku": "ESHOP-2", "inventoryDetails": { "fulfillableQuantity": 8 } } ] }""")));

        var stock = await service.ListWalmartStockAsync(CancellationToken.None);

        Assert.AreEqual(2, stock.Count);
        Assert.AreEqual(5, stock.Single(s => s.Sku == "ESHOP-1").Quantity);
        Assert.AreEqual(8, stock.Single(s => s.Sku == "ESHOP-2").Quantity);
    }

    [TestMethod]
    public async Task GetStockAsync_ThrowsWalmartApiExceptionOnConnectionFailure()
    {
        // A handler that throws models a transport failure.
        var handler = new ThrowingHandler();
        var options = new WalmartApIsClientOptions
        {
            WalletAuth = new OAuth2ClientCredentials { ClientId = "id", ClientSecret = "secret" },
        };
        var client = new WalmartApIsClient(new HttpClient(handler), options);
        var service = new WalmartInventoryService(client, EmptyCatalog(), NullLogger<WalmartInventoryService>.Instance);

        var ex = await Assert.ThrowsExceptionAsync<WalmartApiException>(
            () => service.GetStockAsync("ESHOP-7", CancellationToken.None));

        Assert.IsNull(ex.StatusCode);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("connection reset");
    }
}
