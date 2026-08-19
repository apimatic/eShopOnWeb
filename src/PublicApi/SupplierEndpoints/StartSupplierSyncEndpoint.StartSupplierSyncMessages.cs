using System;

namespace Microsoft.eShopWeb.PublicApi.SupplierEndpoints;

public class StartSupplierSyncRequest : BaseRequest
{
    public StartSupplierSyncRequest(int supplierId)
    {
        SupplierId = supplierId;
    }

    public int SupplierId { get; }
}

public class StartSupplierSyncResponse : BaseResponse
{
    public StartSupplierSyncResponse(Guid correlationId) : base(correlationId)
    {
    }

    public StartSupplierSyncResponse()
    {
    }

    /// <summary>Identifies the sync that was started.</summary>
    public int SyncId { get; set; }

    public int SupplierId { get; set; }

    /// <summary>The sync status at the time it was accepted (always "Running").</summary>
    public string Status { get; set; } = string.Empty;
}
