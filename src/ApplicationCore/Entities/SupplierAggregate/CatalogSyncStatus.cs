namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

/// <summary>
/// Outcome of a supplier catalog sync. Lets a caller tell &mdash; without guessing &mdash;
/// whether the sync is still running, finished having captured the whole listing, or
/// finished having captured only part of it.
/// </summary>
public enum CatalogSyncStatus
{
    /// <summary>The sync has been accepted and is still running.</summary>
    Running = 0,

    /// <summary>Finished; the supplier's whole listing was captured and every product imported.</summary>
    Completed = 1,

    /// <summary>Finished; only part of the listing made it into the catalog (some products
    /// were skipped, or the listing could not be read in full).</summary>
    PartiallyCompleted = 2,

    /// <summary>The sync failed before it could capture the listing.</summary>
    Failed = 3
}
