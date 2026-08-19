namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierCatalogAggregate;

/// <summary>
/// Lifecycle of a supplier catalog sync. A caller can tell, without guessing,
/// whether a sync is still running (<see cref="Pending"/> / <see cref="Running"/>),
/// finished having captured the whole listing (<see cref="Completed"/>),
/// finished having captured only part of it (<see cref="PartiallyCompleted"/>),
/// or failed outright (<see cref="Failed"/>).
/// </summary>
public enum SyncStatus
{
    /// <summary>Accepted and queued, not yet picked up by the worker.</summary>
    Pending = 0,

    /// <summary>The listing is being read and imported right now.</summary>
    Running = 1,

    /// <summary>Finished; every product found on the listing was imported.</summary>
    Completed = 2,

    /// <summary>Finished; only some of the products found were imported.</summary>
    PartiallyCompleted = 3,

    /// <summary>The sync could not complete (e.g. the listing could not be read).</summary>
    Failed = 4
}
