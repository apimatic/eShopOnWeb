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
    /// One of: Pending, Running, Completed (whole listing imported), PartiallyCompleted (only some
    /// found products imported), Failed (listing could not be captured).
    /// </summary>
    public string Status { get; set; }

    /// <summary>How many products the sync found on the supplier's listing.</summary>
    public int ItemsFound { get; set; }

    /// <summary>How many of those products were actually brought into the catalog.</summary>
    public int ItemsImported { get; set; }

    /// <summary>Set when the sync failed, describing what went wrong.</summary>
    public string? ErrorMessage { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
