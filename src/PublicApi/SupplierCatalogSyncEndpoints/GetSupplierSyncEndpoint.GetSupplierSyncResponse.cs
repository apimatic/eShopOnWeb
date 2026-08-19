using System;

namespace Microsoft.eShopWeb.PublicApi.SupplierCatalogSyncEndpoints;

/// <summary>
/// The status and outcome of one sync. <see cref="Status"/> tells the caller whether the sync is
/// still running (<c>Pending</c>/<c>Running</c>), finished having captured the whole listing
/// (<c>Completed</c>), finished having captured only part of it (<c>PartiallyCompleted</c>), or
/// failed (<c>Failed</c>). <see cref="ItemsFound"/> vs <see cref="ItemsImported"/> report exactly
/// how many products were found on the listing versus brought into the catalog.
/// </summary>
public class GetSupplierSyncResponse
{
    public Guid SyncId { get; set; }

    public Guid SupplierId { get; set; }

    public string Status { get; set; } = string.Empty;

    public int ItemsFound { get; set; }

    public int ItemsImported { get; set; }

    /// <summary>Populated only when the sync failed.</summary>
    public string? Error { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
}
