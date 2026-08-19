namespace Microsoft.eShopWeb.PublicApi.SupplierEndpoints;

public class StartSupplierSyncRequest : BaseRequest
{
    public StartSupplierSyncRequest(int supplierId)
    {
        SupplierId = supplierId;
    }

    public int SupplierId { get; }
}
