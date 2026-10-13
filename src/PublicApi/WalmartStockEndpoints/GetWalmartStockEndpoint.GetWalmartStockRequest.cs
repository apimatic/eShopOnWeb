namespace Microsoft.eShopWeb.PublicApi.WalmartStockEndpoints;

public class GetWalmartStockRequest : BaseRequest
{
    public GetWalmartStockRequest(string sku)
    {
        Sku = sku;
    }

    /// <summary>The Walmart seller SKU (bound from the route).</summary>
    public string Sku { get; init; }
}
