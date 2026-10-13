using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.WalmartStockEndpoints;

/// <summary>Body of <c>PUT /api/walmart/stock/{sku}</c> (the SKU comes from the route).</summary>
public class SetWalmartStockRequest
{
    public int Quantity { get; set; }

    /// <summary>Set from the route segment; not supplied in the body.</summary>
    public string Sku { get; set; } = string.Empty;
}

/// <summary>Response of <c>PUT</c> and <c>GET /api/walmart/stock/{sku}</c>.</summary>
public class WalmartStockResponse
{
    public string Sku { get; set; } = string.Empty;
    public int Quantity { get; set; }
}

/// <summary>Response of <c>POST /api/walmart/stock/sync</c>.</summary>
public class SyncWalmartStockResponse
{
    public List<SyncResultItem> Results { get; set; } = new();
}

public class SyncResultItem
{
    public string Sku { get; set; } = string.Empty;

    /// <summary><c>updated</c> or <c>failed</c>.</summary>
    public string Status { get; set; } = string.Empty;
}

/// <summary>Response of <c>GET /api/walmart/stock</c> — Walmart vs eShop, lined up per SKU.</summary>
public class WalmartStockReportResponse
{
    public List<WalmartStockReportItem> Items { get; set; } = new();
}

public class WalmartStockReportItem
{
    public string Sku { get; set; } = string.Empty;

    /// <summary>Walmart's recorded quantity, or null when Walmart holds no such SKU.</summary>
    public int? WalmartQuantity { get; set; }

    /// <summary>eShop's own level, or null when eShop has no such SKU.</summary>
    public int? EshopQuantity { get; set; }
}
