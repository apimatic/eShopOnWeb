namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

/// <summary>
/// Lifecycle state of a supplier catalog sync. Lets a caller tell — without guessing —
/// whether a sync is still running, finished having captured the supplier's whole listing,
/// or finished having captured only part of it.
/// </summary>
public enum SyncStatus
{
    /// <summary>The sync has been accepted and is waiting to be processed.</summary>
    Queued = 0,

    /// <summary>The sync is actively reading the listing and importing items.</summary>
    Running = 1,

    /// <summary>The sync finished and captured the supplier's entire listing into the catalog.</summary>
    Completed = 2,

    /// <summary>
    /// The sync finished but captured only part of the listing — either the reader could not
    /// read the whole listing, or some discovered products could not be imported.
    /// </summary>
    PartiallyCompleted = 3,

    /// <summary>The sync failed before it could complete.</summary>
    Failed = 4
}
