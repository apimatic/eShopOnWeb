using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.CatalogSupplierEndpoints;

public class StartSupplierSyncRequest : BaseRequest
{
    /// <summary>The supplier whose listing should be synced (bound from the route).</summary>
    [FromRoute(Name = "supplierId")]
    public int SupplierId { get; set; }
}
