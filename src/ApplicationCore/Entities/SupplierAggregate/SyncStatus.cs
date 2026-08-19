namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

/// <summary>
/// The lifecycle state of a single supplier catalog sync run.
/// </summary>
public enum SyncStatus
{
    /// <summary>The sync has been created and is waiting to be picked up by the background runner.</summary>
    Queued = 0,

    /// <summary>The sync is actively reading the supplier listing and importing products.</summary>
    Running = 1,

    /// <summary>The sync finished and every product found in the listing was imported into the catalog.</summary>
    Completed = 2,

    /// <summary>The sync finished but only some of the products found were imported into the catalog.</summary>
    PartiallyCompleted = 3,

    /// <summary>The sync could not be completed because reading the supplier listing failed.</summary>
    Failed = 4
}
