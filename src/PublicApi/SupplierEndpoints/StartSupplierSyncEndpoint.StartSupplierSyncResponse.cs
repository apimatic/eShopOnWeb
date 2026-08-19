using System;

namespace Microsoft.eShopWeb.PublicApi.SupplierEndpoints;

public class StartSupplierSyncResponse : BaseResponse
{
    public StartSupplierSyncResponse(Guid correlationId) : base(correlationId) { }

    public StartSupplierSyncResponse() { }

    /// <summary>Identifier of the started sync; poll <c>GET api/catalog/syncs/{syncId}</c> for its outcome.</summary>
    public int SyncId { get; set; }

    public int SupplierId { get; set; }

    /// <summary>Current sync status (e.g. "Pending"); the sync runs asynchronously after this call returns.</summary>
    public string Status { get; set; } = string.Empty;
}
