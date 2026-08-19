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

    /// <summary>Identifies the sync that was started; poll it at GET api/catalog/syncs/{syncId}.</summary>
    public int SyncId { get; set; }

    public int SupplierId { get; set; }

    /// <summary>The sync's status at the moment it was queued.</summary>
    public string Status { get; set; } = string.Empty;
}
