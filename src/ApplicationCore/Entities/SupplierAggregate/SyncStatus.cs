namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

/// <summary>
/// The lifecycle state of a single supplier catalog sync. A caller can tell, without guessing,
/// whether the sync is still running, finished having captured the supplier's whole listing,
/// or finished having captured only part of it.
/// </summary>
public enum SyncStatus
{
    /// <summary>Queued but not yet picked up by the background worker.</summary>
    Pending = 0,

    /// <summary>Currently reading the supplier's listing and importing items.</summary>
    Running = 1,

    /// <summary>Finished; the supplier's whole listing was read and every item was imported.</summary>
    Completed = 2,

    /// <summary>Finished; only part of the listing was captured or some items could not be imported.</summary>
    PartiallyCompleted = 3,

    /// <summary>Failed before any part of the listing could be captured.</summary>
    Failed = 4
}
