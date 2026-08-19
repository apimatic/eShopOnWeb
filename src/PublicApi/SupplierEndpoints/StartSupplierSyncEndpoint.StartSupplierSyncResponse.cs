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

    /// <summary>Identifier of the sync that was started. Poll it via GET /api/catalog/syncs/{syncId}.</summary>
    public int SyncId { get; set; }

    public int SupplierId { get; set; }

    /// <summary>Current status of the sync (it runs in the background, so typically "Pending").</summary>
    public string Status { get; set; } = string.Empty;
}
