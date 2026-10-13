using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.WalmartEndpoints;

/// <summary>Body of PUT /api/walmart/stock/{sku}.</summary>
public class PutWalmartStockRequest
{
    [Range(0, int.MaxValue)]
    public int Quantity { get; set; }
}

/// <summary>Marker request for endpoints that take no input (satisfies the IEndpoint contract).</summary>
public record EmptyRequest;

/// <summary>Bundled inputs for PUT /api/walmart/stock/{sku} (route sku + body quantity).</summary>
public record SetWalmartStockCommand(string Sku, int Quantity);

/// <summary>Bundled input for GET /api/walmart/stock/{sku}.</summary>
public record GetWalmartStockCommand(string Sku);

/// <summary>Response of PUT and GET /api/walmart/stock/{sku}: sku + quantity are top-level.</summary>
public record WalmartStockResponse(string Sku, int Quantity);

/// <summary>One sync outcome: sku + status (updated|failed).</summary>
public record WalmartSyncResultDto(string Sku, string Status);

/// <summary>Response of POST /api/walmart/stock/sync: one result per SKU.</summary>
public record SyncWalmartStockResponse(IReadOnlyList<WalmartSyncResultDto> Results);

/// <summary>
/// One row of the reconciliation report: sku + Walmart's quantity + eShop's quantity, each null
/// on the side that has no such SKU.
/// </summary>
public record WalmartStockComparisonDto(string Sku, int? WalmartQuantity, int? EshopQuantity);

/// <summary>Response of GET /api/walmart/stock: one item per SKU across both sides.</summary>
public record WalmartStockReportResponse(IReadOnlyList<WalmartStockComparisonDto> Items);
