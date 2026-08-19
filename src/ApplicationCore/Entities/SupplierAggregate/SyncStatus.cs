namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

/// <summary>
/// Lifecycle of a supplier catalog sync. Lets a caller tell, without guessing, whether a sync is
/// still running, finished having captured the supplier's whole listing, or finished having
/// captured only part of it.
/// </summary>
public enum SyncStatus
{
    /// <summary>Queued but not yet started.</summary>
    Pending = 0,

    /// <summary>Currently reading the supplier's listing and importing items.</summary>
    Running = 1,

    /// <summary>Finished; every product found in the listing was imported into the catalog.</summary>
    Completed = 2,

    /// <summary>Finished; only some of the products found were imported (the rest could not be captured).</summary>
    PartiallyCompleted = 3,

    /// <summary>The sync could not be completed because of an error.</summary>
    Failed = 4
}
