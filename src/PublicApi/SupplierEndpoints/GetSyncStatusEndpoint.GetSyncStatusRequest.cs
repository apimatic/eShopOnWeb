using System;

namespace Microsoft.eShopWeb.PublicApi.SupplierEndpoints;

public class GetSyncStatusRequest : BaseRequest
{
    public GetSyncStatusRequest(Guid syncId)
    {
        SyncId = syncId;
    }

    public Guid SyncId { get; }
}
