using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi.Walmart;

namespace Microsoft.eShopWeb.PublicApi.WalmartStockEndpoints;

/// <summary>Body of <c>PUT /api/walmart/stock/{sku}</c>: how many of the SKU the shop offers on Walmart.</summary>
public record SetWalmartStockRequest
{
    public string Sku { get; init; } = string.Empty;
    public int Quantity { get; init; }
}

/// <summary>Route-only request for <c>GET /api/walmart/stock/{sku}</c>.</summary>
public record GetWalmartStockRequest(string Sku);

/// <summary>Empty request for <c>POST /api/walmart/stock/sync</c>.</summary>
public record SyncWalmartStockRequest;

/// <summary>Empty request for <c>GET /api/walmart/stock</c>.</summary>
public record GetWalmartStockReportRequest;

/// <summary>Response carrying a SKU and its quantity (top-level <c>sku</c>, <c>quantity</c>).</summary>
public record WalmartStockResponse(string Sku, int Quantity);

/// <summary>Response for the catalog sync (top-level <c>results</c>, one entry per SKU with <c>sku</c> + <c>status</c>).</summary>
public record WalmartStockSyncResponse(IReadOnlyList<WalmartSyncResultItem> Results);

/// <summary>Response for the comparison report (top-level <c>items</c>, each with <c>sku</c>, <c>walmartQuantity</c>, <c>eshopQuantity</c>).</summary>
public record WalmartStockReportResponse(IReadOnlyList<WalmartStockComparisonItem> Items, bool Partial);
