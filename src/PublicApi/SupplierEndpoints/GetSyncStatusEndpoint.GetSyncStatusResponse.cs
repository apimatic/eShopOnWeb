using System;

namespace Microsoft.eShopWeb.PublicApi.SupplierEndpoints;

public class GetSyncStatusResponse : BaseResponse
{
    public GetSyncStatusResponse(Guid correlationId) : base(correlationId) { }

    public GetSyncStatusResponse() { }

    public int SyncId { get; set; }

    public int SupplierId { get; set; }

    /// <summary>
    /// Sync status: "Pending"/"Running" while it is still running, "Completed" when the whole
    /// listing was captured into the catalog, "PartiallyCompleted" when only part of it was, and
    /// "Failed" when the listing could not be read.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Number of products discovered on the supplier's listing.</summary>
    public int ItemsFound { get; set; }

    /// <summary>Number of products actually brought into the catalog (created or updated).</summary>
    public int ItemsImported { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Human-readable note about the outcome (e.g. why a sync was partial or failed).</summary>
    public string? Detail { get; set; }
}
