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

    /// <summary>Identifier of the started sync; poll GET api/catalog/syncs/{syncId} for its outcome.</summary>
    public int SyncId { get; set; }

    public int SupplierId { get; set; }

    /// <summary>Current status of the sync (starts as "Running").</summary>
    public string Status { get; set; }
}
