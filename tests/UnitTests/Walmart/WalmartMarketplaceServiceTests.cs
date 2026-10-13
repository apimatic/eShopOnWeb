using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Walmart;
using Microsoft.eShopWeb.Infrastructure.Walmart;
using Microsoft.Extensions.Logging.Abstractions;
using WalmartApIs;
using WalmartApIs.Core.Configuration;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Walmart;

public class WalmartMarketplaceServiceTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string?, HttpResponseMessage> _responder;
        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string?> Bodies { get; } = new();

        public StubHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            var body = request.Content?.ReadAsStringAsync(ct).Result;
            Bodies.Add(body);
            var response = _responder(request, body);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    // No auth credentials set => no token round-trip; retries disabled => one attempt per call.
    private static WalmartMarketplaceService ServiceWith(StubHandler handler)
    {
        var options = new WalmartApIsClientOptions { Retry = RetryOptions.Disabled() };
        var client = new WalmartApIsClient(new HttpClient(handler), options);
        return new WalmartMarketplaceService(client, NullLogger<WalmartMarketplaceService>.Instance);
    }

    [Fact]
    public async Task SetStockAsync_sends_put_with_quantity_and_returns_it()
    {
        var handler = new StubHandler((_, _) => Json(HttpStatusCode.OK, "{}"));
        var service = ServiceWith(handler);

        var result = await service.SetStockAsync("ESHOP-7", 25, CancellationToken.None);

        Assert.Equal("ESHOP-7", result.Sku);
        Assert.Equal(25, result.Quantity);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Contains("ESHOP-7", request.RequestUri!.AbsoluteUri);
        Assert.Contains("\"quantity\":25", handler.Bodies.Single());
    }

    [Fact]
    public async Task GetStockAsync_reads_on_hand_quantity()
    {
        var handler = new StubHandler((_, _) => Json(HttpStatusCode.OK,
            """{ "sellerSku": "ESHOP-7", "inventoryDetails": { "onHandQuantity": 13 } }"""));
        var service = ServiceWith(handler);

        var result = await service.GetStockAsync("ESHOP-7", CancellationToken.None);

        Assert.Equal("ESHOP-7", result.Sku);
        Assert.Equal(13, result.Quantity);
        Assert.Equal(HttpMethod.Get, handler.Requests.Single().Method);
    }

    [Fact]
    public async Task GetStockAsync_maps_provider_4xx_to_integration_exception_with_same_status()
    {
        var handler = new StubHandler((_, _) => Json(HttpStatusCode.NotFound,
            """{ "errors": [ { "code": "NOT_FOUND", "message": "no such sku" } ] }"""));
        var service = ServiceWith(handler);

        var ex = await Assert.ThrowsAsync<WalmartIntegrationException>(
            () => service.GetStockAsync("ESHOP-404", CancellationToken.None));

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task GetStockAsync_maps_401_to_bad_gateway_not_the_callers_fault()
    {
        var handler = new StubHandler((_, _) => Json(HttpStatusCode.Unauthorized,
            """{ "errors": [ { "code": "AUTH", "message": "bad creds" } ] }"""));
        var service = ServiceWith(handler);

        var ex = await Assert.ThrowsAsync<WalmartIntegrationException>(
            () => service.GetStockAsync("ESHOP-7", CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadGateway, ex.StatusCode);
    }

    [Fact]
    public async Task SetStock_connection_failure_then_reread_confirms_is_settled_as_applied()
    {
        var handler = new StubHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Put)
            {
                throw new HttpRequestException("connection reset");
            }
            // the re-read confirms Walmart recorded the intended quantity
            return Json(HttpStatusCode.OK,
                """{ "sellerSku": "ESHOP-7", "inventoryDetails": { "onHandQuantity": 25 } }""");
        });
        var service = ServiceWith(handler);

        var result = await service.SetStockAsync("ESHOP-7", 25, CancellationToken.None);

        Assert.Equal(25, result.Quantity);
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Put);
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Get); // the settle re-read
    }

    [Fact]
    public async Task ListAllStockAsync_walks_every_page()
    {
        var handler = new StubHandler((request, _) =>
        {
            var hasNextToken = request.RequestUri!.Query.Contains("nextToken", StringComparison.Ordinal);
            return hasNextToken
                ? Json(HttpStatusCode.OK,
                    """{ "inventorySummaries": [ { "sellerSku": "ESHOP-2", "inventoryDetails": { "onHandQuantity": 2 } } ] }""")
                : Json(HttpStatusCode.OK,
                    """{ "inventorySummaries": [ { "sellerSku": "ESHOP-1", "inventoryDetails": { "onHandQuantity": 1 } } ], "pagination": { "nextToken": "t2" } }""");
        });
        var service = ServiceWith(handler);

        var all = await service.ListAllStockAsync(CancellationToken.None);

        Assert.Equal(2, all.Count);
        Assert.Equal(1, all.Single(s => s.Sku == "ESHOP-1").Quantity);
        Assert.Equal(2, all.Single(s => s.Sku == "ESHOP-2").Quantity);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task SyncAsync_reports_updated_and_failed_per_sku()
    {
        var handler = new StubHandler((request, _) =>
            request.RequestUri!.AbsoluteUri.Contains("ESHOP-2", StringComparison.Ordinal)
                ? Json(HttpStatusCode.InternalServerError,
                    """{ "errors": [ { "code": "ERR", "message": "boom" } ] }""")
                : Json(HttpStatusCode.OK, "{}"));
        var service = ServiceWith(handler);

        var desired = new List<WalmartSkuStock> { new("ESHOP-1", 5), new("ESHOP-2", 9) };

        var results = await service.SyncAsync(desired, CancellationToken.None);

        Assert.Equal(WalmartSyncItemResult.StatusUpdated, results.Single(r => r.Sku == "ESHOP-1").Status);
        Assert.Equal(WalmartSyncItemResult.StatusFailed, results.Single(r => r.Sku == "ESHOP-2").Status);
    }
}
