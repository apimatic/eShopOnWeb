namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

/// <summary>
/// Lifecycle/outcome of a single supplier catalog sync.
/// A caller can tell, without guessing, whether the sync is still running
/// (<see cref="Pending"/>/<see cref="Running"/>), finished having captured the
/// supplier's whole listing into the catalog (<see cref="Completed"/>), or finished
/// having captured only part of it (<see cref="PartiallyCompleted"/>/<see cref="Failed"/>).
/// </summary>
public enum CatalogSyncStatus
{
    /// <summary>Queued, not yet picked up by the sync worker.</summary>
    Pending = 0,

    /// <summary>Actively reading the supplier listing and importing items.</summary>
    Running = 1,

    /// <summary>Finished: the whole listing was captured and every product was imported.</summary>
    Completed = 2,

    /// <summary>Finished: only part of the listing made it into the catalog (some products were found but not imported, or the listing could not be read in full).</summary>
    PartiallyCompleted = 3,

    /// <summary>Finished: the sync could not read the listing at all.</summary>
    Failed = 4
}
