using System;

namespace Microsoft.eShopWeb.PublicApi.SupplierCatalogSyncEndpoints;

public class StartSupplierSyncRequest
{
    public StartSupplierSyncRequest(Guid supplierId)
    {
        SupplierId = supplierId;
    }

    public Guid SupplierId { get; }
}
