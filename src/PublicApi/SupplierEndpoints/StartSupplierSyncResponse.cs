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

    /// <summary>Identifies the started sync; use it with GET /api/catalog/syncs/{syncId}.</summary>
    public int SyncId { get; set; }

    public int SupplierId { get; set; }

    /// <summary>Sync status at the moment it was queued (always "Running").</summary>
    public string Status { get; set; }
}
