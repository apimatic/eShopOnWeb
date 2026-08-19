namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

/// <summary>
/// Lifecycle of a single supplier catalog sync. A caller can tell, without guessing, whether
/// the sync is still running, finished having captured the supplier's whole listing, or
/// finished having captured only part of it.
/// </summary>
public enum SyncStatus
{
    /// <summary>Queued, not yet picked up by a worker.</summary>
    Pending = 0,

    /// <summary>Currently reading the supplier's listing and importing items.</summary>
    Running = 1,

    /// <summary>Finished; every product found was imported into the catalog.</summary>
    Completed = 2,

    /// <summary>Finished, but only some of the products found were imported (the rest failed).</summary>
    PartiallyCompleted = 3,

    /// <summary>The sync failed before it could capture the listing (e.g. the page could not be read).</summary>
    Failed = 4
}
