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
    /// One of: Pending, Running, Completed (whole listing captured), PartiallyCompleted (only
    /// part captured), Failed (listing could not be read).
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Number of products found in the supplier's listing.</summary>
    public int ItemsFound { get; set; }

    /// <summary>Number of products actually brought into the catalog.</summary>
    public int ItemsImported { get; set; }

    public string? Error { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
}
