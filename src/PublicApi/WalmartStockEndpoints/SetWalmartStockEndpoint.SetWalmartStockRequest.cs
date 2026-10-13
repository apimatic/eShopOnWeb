using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.WalmartStockEndpoints;

public class SetWalmartStockRequest : BaseRequest
{
    public SetWalmartStockRequest()
    {
    }

    public SetWalmartStockRequest(string sku, int quantity)
    {
        Sku = sku;
        Quantity = quantity;
    }

    /// <summary>The Walmart seller SKU (bound from the route).</summary>
    public string Sku { get; init; } = string.Empty;

    /// <summary>The quantity to record on Walmart for this SKU.</summary>
    [Range(0, int.MaxValue)]
    public int Quantity { get; init; }
}
