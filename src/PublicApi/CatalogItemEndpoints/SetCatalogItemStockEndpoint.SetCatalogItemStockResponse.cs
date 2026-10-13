using System;

namespace Microsoft.eShopWeb.PublicApi.CatalogItemEndpoints;

public class SetCatalogItemStockResponse : BaseResponse
{
    public SetCatalogItemStockResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SetCatalogItemStockResponse()
    {
    }

    public int CatalogItemId { get; set; }

    /// <summary>eShop's stock level for the item after the update.</summary>
    public int Quantity { get; set; }
}
