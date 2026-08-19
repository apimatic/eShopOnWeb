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
    /// One of: Queued, Running, Completed (captured the whole listing),
    /// PartiallyCompleted (captured only part), Failed.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>How many products the sync discovered on the supplier's listing.</summary>
    public int ItemsFound { get; set; }

    /// <summary>How many discovered products were actually imported into the catalog.</summary>
    public int ItemsImported { get; set; }

    /// <summary>Human-readable explanation for a partial or failed outcome; null when fully completed.</summary>
    public string? StatusDetail { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
}
