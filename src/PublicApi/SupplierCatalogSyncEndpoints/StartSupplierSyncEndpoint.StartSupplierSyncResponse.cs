using System;

namespace Microsoft.eShopWeb.PublicApi.SupplierCatalogSyncEndpoints;

public class StartSupplierSyncResponse
{
    /// <summary>The identifier of the newly started sync; poll it via GET api/catalog/syncs/{syncId}.</summary>
    public Guid SyncId { get; set; }

    public Guid SupplierId { get; set; }

    /// <summary>The sync status at the moment it was accepted (initially "Pending").</summary>
    public string Status { get; set; } = string.Empty;
}
