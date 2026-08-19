using System;

namespace Microsoft.eShopWeb.PublicApi.CatalogSupplierEndpoints;

public class StartSupplierSyncResponse : BaseResponse
{
    public StartSupplierSyncResponse(Guid correlationId) : base(correlationId)
    {
    }

    public StartSupplierSyncResponse()
    {
    }

    /// <summary>The identifier of the started sync; poll GET api/catalog/syncs/{syncId} for progress.</summary>
    public int SyncId { get; set; }

    public int SupplierId { get; set; }

    /// <summary>The sync's status at the moment it was queued (always "Running").</summary>
    public string Status { get; set; } = string.Empty;
}
