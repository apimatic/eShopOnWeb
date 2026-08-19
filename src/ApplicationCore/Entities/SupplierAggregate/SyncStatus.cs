namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

/// <summary>
/// Lifecycle of a supplier catalog sync. A caller can tell, without guessing, whether the sync is
/// still running, finished having captured the supplier's whole listing, or finished having
/// captured only part of it.
/// </summary>
public enum SyncStatus
{
    /// <summary>The sync has been queued but has not started reading the listing yet.</summary>
    Pending = 0,

    /// <summary>The sync is actively reading the listing and importing products.</summary>
    Running = 1,

    /// <summary>The sync finished and every product found on the listing was imported into the catalog.</summary>
    Completed = 2,

    /// <summary>The sync finished but only some of the products found could be imported into the catalog.</summary>
    PartiallyCompleted = 3,

    /// <summary>The sync could not be completed (for example the listing could not be read).</summary>
    Failed = 4
}
