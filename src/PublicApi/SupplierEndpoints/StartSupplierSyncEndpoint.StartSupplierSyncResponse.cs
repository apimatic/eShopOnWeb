using System;

namespace Microsoft.eShopWeb.PublicApi.SupplierEndpoints;

public class StartSupplierSyncResponse : BaseResponse
{
    public StartSupplierSyncResponse(Guid correlationId) : base(correlationId)
    {
    }

    public StartSupplierSyncResponse()
    {
    }

    /// <summary>The id identifying the started sync; poll it at GET /api/catalog/syncs/{syncId}.</summary>
    public int SyncId { get; set; }

    public int SupplierId { get; set; }
}
