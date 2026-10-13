using System;

namespace Microsoft.eShopWeb.PublicApi.CatalogItemEndpoints;

public class UpdateCatalogItemStockResponse : BaseResponse
{
    public UpdateCatalogItemStockResponse(Guid correlationId) : base(correlationId)
    {
    }

    public UpdateCatalogItemStockResponse()
    {
    }

    public int CatalogItemId { get; set; }

    /// <summary>eShop's stock level for the item after the update.</summary>
    public int Quantity { get; set; }
}
