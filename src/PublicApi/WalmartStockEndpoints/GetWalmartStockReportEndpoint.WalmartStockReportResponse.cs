using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.WalmartStockEndpoints;

public class WalmartStockReportResponse : BaseResponse
{
    /// <summary>One entry per SKU, across the union of Walmart SKUs and eShop catalog items.</summary>
    public List<WalmartStockComparisonDto> Items { get; set; } = new();
}

public class WalmartStockComparisonDto
{
    public string Sku { get; set; } = string.Empty;

    /// <summary>Walmart's recorded quantity, or <c>null</c> when Walmart has no such SKU.</summary>
    public int? WalmartQuantity { get; set; }

    /// <summary>eShop's own level, or <c>null</c> when no eShop catalog item maps to this SKU.</summary>
    public int? EshopQuantity { get; set; }
}
