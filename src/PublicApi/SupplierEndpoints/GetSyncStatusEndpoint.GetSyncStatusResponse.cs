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
    /// "Running" (still going), "Completed" (whole listing captured), "Partial" (only part captured),
    /// or "Failed" (listing could not be read).
    /// </summary>
    public string Status { get; set; }

    /// <summary>How many products the sync found on the supplier's listing.</summary>
    public int ItemsFound { get; set; }

    /// <summary>How many of those products were actually brought into the catalog.</summary>
    public int ItemsImported { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public string? Error { get; set; }
}
