using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.WalmartStockEndpoints;

public class SyncWalmartStockResponse : BaseResponse
{
    /// <summary>One entry per SKU pushed, with the outcome Walmart reported.</summary>
    public List<WalmartSyncResultDto> Results { get; set; } = new();
}

public class WalmartSyncResultDto
{
    public string Sku { get; set; } = string.Empty;

    /// <summary>Either <c>updated</c> or <c>failed</c>.</summary>
    public string Status { get; set; } = string.Empty;
}
