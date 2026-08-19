namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

/// <summary>
/// Lifecycle of a single supplier catalog sync. A caller can tell, without guessing,
/// whether the sync is still running, finished having captured the supplier's whole
/// listing, or finished having captured only part of it.
/// </summary>
public enum SyncStatus
{
    /// <summary>The sync has been accepted and is still running.</summary>
    Running = 0,

    /// <summary>The sync finished and every product found on the listing was brought into the catalog.</summary>
    Completed = 1,

    /// <summary>The sync finished but only some of the products found could be brought into the catalog.</summary>
    Partial = 2,

    /// <summary>The sync could not read the listing at all (e.g. the supplier page was unreachable).</summary>
    Failed = 3
}
