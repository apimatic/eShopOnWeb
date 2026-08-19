using System;

namespace Microsoft.eShopWeb.PublicApi.SupplierEndpoints;

public class StartSupplierSyncResponse : BaseResponse
{
    public StartSupplierSyncResponse(Guid correlationId) : base(correlationId) { }

    public StartSupplierSyncResponse() { }

    /// <summary>The identifier of the started sync.</summary>
    public Guid SyncId { get; set; }

    public Guid SupplierId { get; set; }

    /// <summary>The sync's current status (it starts queued and runs in the background).</summary>
    public string Status { get; set; } = string.Empty;
}
