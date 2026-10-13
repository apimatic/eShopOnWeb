using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.WalmartStockEndpoints;

/// <summary>
/// A stand-in for a Walmart-compatible inventory service. Emulates the wm-v4 Inventory routes
/// (<c>PUT/GET /inventory/v4/items/{sku}</c>, <c>GET /inventory/v4/items</c>) plus the OAuth token endpoint,
/// holding on-hand quantities in memory so set/get/sync/report round-trip realistically — all without a
/// live Walmart. Test-controllable failure switches exercise the error and unknown-outcome paths.
/// </summary>
public sealed class FakeWalmartHandler : HttpMessageHandler
{
    private const string ItemsMarker = "/inventory/v4/items";

    /// <summary>Walmart's own record of on-hand quantity per seller SKU.</summary>
    public Dictionary<string, int> OnHand { get; } = new(StringComparer.Ordinal);

    /// <summary>Every request the handler saw (method + absolute path), for assertions.</summary>
    public List<(HttpMethod Method, string Path)> Calls { get; } = new();

    /// <summary>When set, a PUT update for a SKU this returns true for answers 400 (a per-SKU failure).</summary>
    public Func<string, bool>? FailUpdateForSku { get; set; }

    /// <summary>When true, every PUT update throws a transport error (to drive the unknown-outcome path).</summary>
    public bool FailAllUpdatesWithTransportError { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        Calls.Add((request.Method, path));

        // OAuth token endpoint (WalletAuth).
        if (request.Method == HttpMethod.Post && path.EndsWith("/auth/token", StringComparison.Ordinal))
        {
            return Json(HttpStatusCode.OK, """{"access_token":"fake-token","token_type":"Bearer","expires_in":3600}""");
        }

        var skuSegment = SkuSegment(path);

        // PUT /inventory/v4/items/{sku} — set on-hand.
        if (request.Method == HttpMethod.Put && skuSegment is not null)
        {
            if (FailAllUpdatesWithTransportError)
            {
                throw new HttpRequestException("simulated connection reset");
            }

            if (FailUpdateForSku?.Invoke(skuSegment) == true)
            {
                return Json(HttpStatusCode.BadRequest,
                    """{"errors":[{"code":"INVALID_SKU","message":"unknown sku"}]}""");
            }

            var body = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
            var quantity = ReadQuantity(body);
            OnHand[skuSegment] = quantity;
            return Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { sellerSku = skuSegment, quantity }));
        }

        // GET /inventory/v4/items/{sku} — single summary.
        if (request.Method == HttpMethod.Get && skuSegment is not null)
        {
            var quantity = OnHand.TryGetValue(skuSegment, out var q) ? q : 0;
            return Json(HttpStatusCode.OK, SummaryJson(skuSegment, quantity));
        }

        // GET /inventory/v4/items — all summaries (single page).
        if (request.Method == HttpMethod.Get && path.EndsWith(ItemsMarker, StringComparison.Ordinal))
        {
            var summaries = OnHand.Select(kv => new
            {
                sellerSku = kv.Key,
                inventoryDetails = new { onHandQuantity = kv.Value },
            });
            return Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { inventorySummaries = summaries }));
        }

        return Json(HttpStatusCode.NotFound, """{"errors":[{"code":"NOT_FOUND","message":"no route"}]}""");
    }

    private static string? SkuSegment(string path)
    {
        var idx = path.IndexOf(ItemsMarker, StringComparison.Ordinal);
        if (idx < 0) return null;
        var rest = path.Substring(idx + ItemsMarker.Length);
        if (rest.Length <= 1 || rest[0] != '/') return null;
        return Uri.UnescapeDataString(rest.Substring(1));
    }

    private static int ReadQuantity(string body)
    {
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("quantity", out var q) ? q.GetInt32() : 0;
    }

    private static string SummaryJson(string sku, int quantity) =>
        JsonSerializer.Serialize(new { sellerSku = sku, inventoryDetails = new { onHandQuantity = quantity } });

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
