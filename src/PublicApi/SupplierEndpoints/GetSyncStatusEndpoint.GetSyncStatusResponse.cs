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
    /// The sync's state: Pending, Running, Completed (whole listing captured),
    /// PartiallyCompleted (only some products imported), or Failed.
    /// </summary>
    public string Status { get; set; }

    /// <summary>How many products were found on the supplier's listing.</summary>
    public int ItemsFound { get; set; }

    /// <summary>How many of those products were actually brought into the catalog.</summary>
    public int ItemsImported { get; set; }

    public string? ExternalJobId { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTimeOffset CreatedDate { get; set; }

    public DateTimeOffset? StartedDate { get; set; }

    public DateTimeOffset? CompletedDate { get; set; }
}
