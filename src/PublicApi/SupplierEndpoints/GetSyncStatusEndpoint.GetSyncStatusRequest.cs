namespace Microsoft.eShopWeb.PublicApi.SupplierEndpoints;

public class GetSyncStatusRequest : BaseRequest
{
    public GetSyncStatusRequest(int syncId)
    {
        SyncId = syncId;
    }

    public int SyncId { get; }
}
