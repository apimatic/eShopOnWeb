using System;

namespace Microsoft.eShopWeb.PublicApi.SupplierEndpoints;

public class StartSupplierSyncRequest : BaseRequest
{
    public StartSupplierSyncRequest(Guid supplierId)
    {
        SupplierId = supplierId;
    }

    public Guid SupplierId { get; }
}
