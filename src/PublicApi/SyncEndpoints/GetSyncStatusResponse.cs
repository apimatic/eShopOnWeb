using System;

namespace Microsoft.eShopWeb.PublicApi.SyncEndpoints;

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
    /// One of: "Running", "Completed" (whole listing captured), "PartiallyCompleted" (only part
    /// captured), or "Failed" (listing could not be read).
    /// </summary>
    public string Status { get; set; }

    /// <summary>How many products were found on the supplier's listing.</summary>
    public int ItemsFound { get; set; }

    /// <summary>How many of those products were actually brought into the catalog.</summary>
    public int ItemsImported { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Populated only when Status is "Failed".</summary>
    public string? Error { get; set; }
}
