namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

/// <summary>
/// The lifecycle state and outcome of a single supplier catalog sync.
/// </summary>
public enum SupplierSyncStatus
{
    /// <summary>The sync has been queued but has not started reading the listing yet.</summary>
    Pending = 0,

    /// <summary>The sync is actively reading the listing and importing items.</summary>
    Running = 1,

    /// <summary>The sync finished and every product found in the listing was imported into the catalog.</summary>
    Completed = 2,

    /// <summary>The sync finished but only some of the products found were imported into the catalog.</summary>
    PartiallyCompleted = 3,

    /// <summary>The sync could not read the supplier's listing at all and imported nothing.</summary>
    Failed = 4
}
