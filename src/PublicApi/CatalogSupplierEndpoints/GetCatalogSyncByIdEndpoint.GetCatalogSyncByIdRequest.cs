using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.CatalogSupplierEndpoints;

public class GetCatalogSyncByIdRequest : BaseRequest
{
    /// <summary>The sync to report on (bound from the route).</summary>
    [FromRoute(Name = "syncId")]
    public int SyncId { get; set; }
}
