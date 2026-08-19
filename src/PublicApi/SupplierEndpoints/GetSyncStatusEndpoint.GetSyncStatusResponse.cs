using System;

namespace Microsoft.eShopWeb.PublicApi.SupplierEndpoints;

public class GetSyncStatusResponse : BaseResponse
{
    public GetSyncStatusResponse(Guid correlationId) : base(correlationId) { }

    public GetSyncStatusResponse() { }

    public Guid SyncId { get; set; }

    public Guid SupplierId { get; set; }

    /// <summary>
    /// One of: Pending, Running, Completed, PartiallyImported, Failed. Completed means the whole
    /// listing was captured; PartiallyImported means only some found products were imported.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>How many products were found in the supplier's listing.</summary>
    public int ItemsFound { get; set; }

    /// <summary>How many of those products were actually brought into the catalog.</summary>
    public int ItemsImported { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Human-readable detail about the outcome (e.g. why it was partial or failed).</summary>
    public string? Detail { get; set; }
}
