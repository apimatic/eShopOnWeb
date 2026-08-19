using System;

namespace Microsoft.eShopWeb.PublicApi.CatalogSupplierEndpoints;

public class CatalogSyncResponse : BaseResponse
{
    public CatalogSyncResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CatalogSyncResponse()
    {
    }

    public int SyncId { get; set; }

    public int SupplierId { get; set; }

    /// <summary>
    /// "Running" (still in flight), "Completed" (whole listing captured), "PartiallyCompleted"
    /// (only some found products imported) or "Failed" (listing could not be read).
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>How many distinct products were found in the supplier's listing.</summary>
    public int ItemsFound { get; set; }

    /// <summary>How many of the found products were actually brought into the catalog.</summary>
    public int ItemsImported { get; set; }

    public DateTimeOffset StartedDate { get; set; }

    public DateTimeOffset? CompletedDate { get; set; }

    public string? ErrorMessage { get; set; }
}
