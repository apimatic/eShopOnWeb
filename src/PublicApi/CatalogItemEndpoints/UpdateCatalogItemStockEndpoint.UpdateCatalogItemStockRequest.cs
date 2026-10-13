using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.CatalogItemEndpoints;

public class UpdateCatalogItemStockRequest : BaseRequest
{
    public UpdateCatalogItemStockRequest()
    {
    }

    public UpdateCatalogItemStockRequest(int catalogItemId, int quantity)
    {
        CatalogItemId = catalogItemId;
        Quantity = quantity;
    }

    /// <summary>The catalog item whose eShop stock level is being set (bound from the route).</summary>
    public int CatalogItemId { get; init; }

    /// <summary>The absolute stock level to set for the item.</summary>
    [Range(0, int.MaxValue)]
    public int Quantity { get; init; }
}
