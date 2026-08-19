namespace Microsoft.eShopWeb.PublicApi.SyncEndpoints;

public class GetSyncStatusRequest : BaseRequest
{
    public GetSyncStatusRequest(int syncId)
    {
        SyncId = syncId;
    }

    public int SyncId { get; set; }
}
