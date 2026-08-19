using System;

namespace Microsoft.eShopWeb.PublicApi.SupplierEndpoints;

public class GetSupplierSyncResponse : BaseResponse
{
    public GetSupplierSyncResponse(Guid correlationId) : base(correlationId)
    {
    }

    public GetSupplierSyncResponse()
    {
    }

    public int SyncId { get; set; }

    public int SupplierId { get; set; }

    /// <summary>
    /// One of: Pending, Running, Completed (whole listing captured and imported),
    /// PartiallyCompleted (only part captured/imported), Failed.
    /// </summary>
    public string Status { get; set; }

    /// <summary>How many products were found in the supplier's listing.</summary>
    public int ItemsFound { get; set; }

    /// <summary>How many of those products were actually brought into the catalog.</summary>
    public int ItemsImported { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Populated only when the sync failed.</summary>
    public string? Error { get; set; }
}
