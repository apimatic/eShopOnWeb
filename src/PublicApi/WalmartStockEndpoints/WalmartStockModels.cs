using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.WalmartStockEndpoints;

/// <summary>Body of PUT /api/walmart/stock/{sku}. The SKU itself comes from the route.</summary>
public class SetWalmartStockRequest
{
    /// <summary>The number of units the shop offers on Walmart for this SKU. Must be ≥ 0.</summary>
    public int Quantity { get; set; }

    /// <summary>Populated from the route, not the request body.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Sku { get; set; } = string.Empty;
}

/// <summary>Response for PUT and GET /api/walmart/stock/{sku}.</summary>
public class WalmartSkuStockResponse
{
    public string Sku { get; set; } = string.Empty;
    public int Quantity { get; set; }
}

/// <summary>Response for POST /api/walmart/stock/sync.</summary>
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

/// <summary>Response for GET /api/walmart/stock (reconciliation report).</summary>
public class WalmartStockReportResponse
{
    public List<WalmartStockReportItem> Items { get; set; } = new();
}

public class WalmartStockReportItem
{
    public string Sku { get; set; } = string.Empty;

    /// <summary>Quantity Walmart records for the SKU; <c>null</c> when Walmart has no such SKU.</summary>
    public int? WalmartQuantity { get; set; }

    /// <summary>eShop's own level for the matching catalog item; <c>null</c> when eShop has no such SKU.</summary>
    public int? EshopQuantity { get; set; }
}
