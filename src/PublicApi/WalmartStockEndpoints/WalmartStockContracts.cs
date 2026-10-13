using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.WalmartStockEndpoints;

/// <summary>Body for setting a single SKU's quantity on Walmart (the SKU is filled from the route).</summary>
public class SetWalmartStockRequest
{
    /// <summary>Filled from the route; not read from the request body.</summary>
    public string Sku { get; set; } = string.Empty;

    public int Quantity { get; set; }
}

/// <summary>Response of PUT/GET a single SKU — <c>sku</c> and <c>quantity</c> are top-level.</summary>
public class WalmartStockResponse
{
    public string Sku { get; set; } = string.Empty;
    public int Quantity { get; set; }
}

/// <summary>Response of the catalog sync — <c>results</c>, one entry per SKU.</summary>
public class SyncWalmartStockResponse
{
    public List<SyncResultItem> Results { get; set; } = new();
}

public class SyncResultItem
{
    public string Sku { get; set; } = string.Empty;

    /// <summary>Either <c>updated</c> or <c>failed</c>.</summary>
    public string Status { get; set; } = string.Empty;
}

/// <summary>Response of the reconciliation report — <c>items</c>, one entry per SKU.</summary>
public class WalmartStockReportResponse
{
    public List<WalmartStockReportItem> Items { get; set; } = new();
}

public class WalmartStockReportItem
{
    public string Sku { get; set; } = string.Empty;

    /// <summary>Walmart's recorded quantity, or null when Walmart has no such SKU.</summary>
    public int? WalmartQuantity { get; set; }

    /// <summary>eShop's own level for the matching catalog item, or null when eShop has no such SKU.</summary>
    public int? EshopQuantity { get; set; }
}
