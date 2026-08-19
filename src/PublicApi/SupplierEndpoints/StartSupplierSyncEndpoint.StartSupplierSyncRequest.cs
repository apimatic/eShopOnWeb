namespace Microsoft.eShopWeb.PublicApi.SupplierEndpoints;

public class StartSupplierSyncRequest : BaseRequest
{
    public int SupplierId { get; init; }

    public StartSupplierSyncRequest(int supplierId)
    {
        SupplierId = supplierId;
    }
}
