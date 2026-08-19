namespace Microsoft.eShopWeb.PublicApi.SupplierEndpoints;

public class GetSupplierSyncRequest : BaseRequest
{
    public GetSupplierSyncRequest(int syncId)
    {
        SyncId = syncId;
    }

    public int SyncId { get; set; }
}
