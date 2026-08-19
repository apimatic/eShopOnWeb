namespace Microsoft.eShopWeb.PublicApi.SupplierEndpoints;

public class GetSyncStatusRequest : BaseRequest
{
    public int SyncId { get; init; }

    public GetSyncStatusRequest(int syncId)
    {
        SyncId = syncId;
    }
}
