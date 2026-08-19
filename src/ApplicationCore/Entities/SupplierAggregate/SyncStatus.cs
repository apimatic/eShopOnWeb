namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

/// <summary>
/// The lifecycle state of a supplier catalog sync. A caller can distinguish, without guessing,
/// whether a sync is still in flight (<see cref="Running"/>), finished having captured the
/// supplier's whole listing (<see cref="Completed"/>), finished having captured only part of it
/// (<see cref="PartiallyCompleted"/>), or could not read the listing at all (<see cref="Failed"/>).
/// </summary>
public enum SyncStatus
{
    /// <summary>The sync has been queued or is actively reading and importing the listing.</summary>
    Running = 1,

    /// <summary>The listing was read and every product found was imported into the catalog.</summary>
    Completed = 2,

    /// <summary>The listing was read but only some of the products found were imported.</summary>
    PartiallyCompleted = 3,

    /// <summary>The listing could not be read; nothing was imported.</summary>
    Failed = 4
}
