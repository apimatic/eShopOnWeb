namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

/// <summary>
/// Lifecycle of a single supplier-catalog sync. The three terminal outcomes let a caller tell,
/// without guessing, whether the whole listing was captured or only part of it.
/// </summary>
public enum SupplierSyncStatus
{
    /// <summary>Accepted and queued, not yet started.</summary>
    Pending = 0,

    /// <summary>Actively reading the supplier's listing and importing items.</summary>
    Running = 1,

    /// <summary>Finished; every product found in the listing was brought into the catalog.</summary>
    Completed = 2,

    /// <summary>Finished; the listing was read but only some of the products found could be imported.</summary>
    PartiallyCompleted = 3,

    /// <summary>The sync could not be completed (e.g. the listing could not be read).</summary>
    Failed = 4
}
