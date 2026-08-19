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

    /// <summary>Identifier of the sync that was started.</summary>
    public int SyncId { get; set; }

    public int SupplierId { get; set; }

    /// <summary>The sync's initial status (it runs in the background after this call returns).</summary>
    public string Status { get; set; } = string.Empty;
}
