using System;

namespace Microsoft.eShopWeb.PublicApi.SupplierCatalogSyncEndpoints;

public class GetSupplierSyncRequest
{
    public GetSupplierSyncRequest(Guid syncId)
    {
        SyncId = syncId;
    }

    public Guid SyncId { get; }
}
