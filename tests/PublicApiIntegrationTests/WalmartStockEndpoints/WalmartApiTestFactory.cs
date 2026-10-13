using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace PublicApiIntegrationTests.WalmartStockEndpoints;

/// <summary>
/// Boots PublicApi with the Walmart SDK pointed at an in-process, stateful Walmart-compatible stub
/// (via the default IHttpClientFactory handler the SDK resolves). This exercises the full
/// endpoint → service → SDK → HTTP path without a live Walmart, and keeps per-SKU state so a sync
/// is visible to a later report.
/// </summary>
public class WalmartApiTestFactory : WebApplicationFactory<Program>
{
    public WalmartStubHandler Stub { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["UseOnlyInMemoryDatabase"] = "true",
                ["Walmart:ClientId"] = "test-client-id",
                ["Walmart:ClientSecret"] = "test-client-secret",
                // Point the SDK at the stub's host; path is preserved by the override logic.
                ["Walmart:BaseUrl"] = "http://walmart.stub.local:1234"
            });
        });

        builder.ConfigureServices(services =>
        {
            // The SDK client resolves the default (unnamed) IHttpClientFactory client.
            services.AddHttpClient(Options.DefaultName)
                .ConfigurePrimaryHttpMessageHandler(() => Stub);
        });
    }
}

/// <summary>A minimal stateful Walmart Marketplace stub: token + the four Inventory operations.</summary>
public sealed class WalmartStubHandler : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, int> _inventory = new(StringComparer.Ordinal);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.AbsolutePath;
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct);

        // WalletAuth token endpoint.
        if (path.EndsWith("/auth/token", StringComparison.Ordinal))
        {
            return Json("""{"access_token":"stub-token","token_type":"Bearer","expires_in":3600}""");
        }

        // POST /inventory/v4/items/bulk
        if (request.Method == HttpMethod.Post && path.EndsWith("/items/bulk", StringComparison.Ordinal))
        {
            var succeeded = new List<object>();
            using var doc = JsonDocument.Parse(body);
            foreach (var item in doc.RootElement.GetProperty("items").EnumerateArray())
            {
                var sku = item.GetProperty("sellerSku").GetString()!;
                _inventory[sku] = item.GetProperty("quantity").GetInt32();
                succeeded.Add(new { sellerSku = sku });
            }
            return Json(new { succeeded, errors = Array.Empty<object>() });
        }

        // GET /inventory/v4/items  (summaries — no {sku} segment after items)
        if (request.Method == HttpMethod.Get && path.EndsWith("/inventory/v4/items", StringComparison.Ordinal))
        {
            var summaries = _inventory.Select(kvp => new
            {
                sellerSku = kvp.Key,
                inventoryDetails = new { onHandQuantity = kvp.Value }
            });
            return Json(new { pagination = new { }, inventorySummaries = summaries });
        }

        // GET or PUT /inventory/v4/items/{sku}
        var marker = "/inventory/v4/items/";
        var idx = path.IndexOf(marker, StringComparison.Ordinal);
        if (idx >= 0)
        {
            var sku = Uri.UnescapeDataString(path[(idx + marker.Length)..]);
            if (request.Method == HttpMethod.Put)
            {
                using var doc = JsonDocument.Parse(body);
                var qty = doc.RootElement.GetProperty("quantity").GetInt32();
                _inventory[sku] = qty;
                return Json(new { sellerSku = sku, quantity = qty });
            }
            if (request.Method == HttpMethod.Get)
            {
                var qty = _inventory.TryGetValue(sku, out var v) ? v : 0;
                return Json(new { sellerSku = sku, inventoryDetails = new { onHandQuantity = qty } });
            }
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Json(object payload) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
