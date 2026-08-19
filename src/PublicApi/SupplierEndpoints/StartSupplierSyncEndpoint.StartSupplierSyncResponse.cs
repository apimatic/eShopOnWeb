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

    /// <summary>The id that identifies this sync run; poll it via GET api/catalog/syncs/{syncId}.</summary>
    public int SyncId { get; set; }

    public int SupplierId { get; set; }

    /// <summary>The sync's status at the moment it was accepted (initially "Pending").</summary>
    public string Status { get; set; }
}
