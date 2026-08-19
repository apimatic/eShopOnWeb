using System;

namespace Microsoft.eShopWeb.PublicApi.SupplierEndpoints;

public class GetCatalogSyncRequest : BaseRequest
{
    public GetCatalogSyncRequest(int syncId)
    {
        SyncId = syncId;
    }

    public int SyncId { get; }
}

public class GetCatalogSyncResponse : BaseResponse
{
    public GetCatalogSyncResponse(Guid correlationId) : base(correlationId)
    {
    }

    public GetCatalogSyncResponse()
    {
    }

    public int SyncId { get; set; }

    public int SupplierId { get; set; }

    /// <summary>
    /// One of "Running", "Completed", "PartiallyCompleted", or "Failed" &mdash; lets a caller tell,
    /// without guessing, whether the sync is still running, captured the whole listing, or only part.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Number of products discovered in the supplier's listing.</summary>
    public int ItemsFound { get; set; }

    /// <summary>Number of products actually brought into the catalog.</summary>
    public int ItemsImported { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Populated only when the sync failed.</summary>
    public string? ErrorMessage { get; set; }
}
