namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

/// <summary>
/// Lifecycle of a supplier catalog sync. The terminal states let a caller tell, without
/// guessing, whether the whole listing was captured (<see cref="Completed"/>), only part of
/// it made it into the catalog (<see cref="PartiallyImported"/>), or the sync could not run
/// at all (<see cref="Failed"/>).
/// </summary>
public enum SyncStatus
{
    /// <summary>Accepted and queued, not yet started.</summary>
    Pending = 0,

    /// <summary>Currently reading the supplier's listing and importing products.</summary>
    Running = 1,

    /// <summary>Finished; every product found in the listing was brought into the catalog.</summary>
    Completed = 2,

    /// <summary>Finished; some products were found but could not be brought into the catalog.</summary>
    PartiallyImported = 3,

    /// <summary>The listing could not be read or the sync errored before completing.</summary>
    Failed = 4
}
