using System;

namespace Microsoft.eShopWeb.PublicApi.SupplierEndpoints;

public class GetSyncStatusResponse : BaseResponse
{
    public GetSyncStatusResponse(Guid correlationId) : base(correlationId)
    {
    }

    public GetSyncStatusResponse()
    {
    }

    public int SyncId { get; set; }
    public int SupplierId { get; set; }

    /// <summary>
    /// Sync status: Pending or Running (still running), Completed (whole listing captured),
    /// PartiallyCompleted (only part captured), or Failed.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Number of products found in the supplier's listing.</summary>
    public int ItemsFound { get; set; }

    /// <summary>Number of found products actually imported into the catalog.</summary>
    public int ItemsImported { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Populated only when the sync failed.</summary>
    public string? ErrorMessage { get; set; }
}
