using System;

namespace Microsoft.eShopWeb.PublicApi.SupplierEndpoints;

/// <summary>
/// The status and outcome of a supplier-catalog sync. <see cref="Status"/> tells a caller whether
/// the sync is still running, finished capturing the whole listing (<c>Completed</c>), or finished
/// capturing only part of it (<c>PartiallyCompleted</c>); <see cref="ItemsFound"/> and
/// <see cref="ItemsImported"/> give the exact counts.
/// </summary>
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

    /// <summary>One of: Pending, Running, Completed, PartiallyCompleted, Failed.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>How many products were discovered in the supplier's listing.</summary>
    public int ItemsFound { get; set; }

    /// <summary>How many of those products were actually brought into the catalog.</summary>
    public int ItemsImported { get; set; }

    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Populated when the sync failed.</summary>
    public string? ErrorMessage { get; set; }
}
