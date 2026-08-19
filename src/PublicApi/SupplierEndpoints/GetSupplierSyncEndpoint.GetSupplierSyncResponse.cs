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
    /// The sync's status: <c>Queued</c>/<c>Running</c> while still in progress, <c>Completed</c>
    /// when the whole listing was captured and imported, <c>PartiallyCompleted</c> when only part
    /// of it was, or <c>Failed</c> when the listing could not be read.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Number of products found in the supplier's listing.</summary>
    public int ItemsFound { get; set; }

    /// <summary>Number of products actually imported into the catalog.</summary>
    public int ItemsImported { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Set when the sync failed; null otherwise.</summary>
    public string? ErrorMessage { get; set; }
}
