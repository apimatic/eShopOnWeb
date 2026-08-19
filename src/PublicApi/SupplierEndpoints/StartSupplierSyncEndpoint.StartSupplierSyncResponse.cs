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

    /// <summary>Identifier of the started sync; poll GET /api/catalog/syncs/{syncId} for progress.</summary>
    public int SyncId { get; set; }

    public int SupplierId { get; set; }

    /// <summary>Current status of the sync (e.g. Pending / Running).</summary>
    public string Status { get; set; } = string.Empty;
}
