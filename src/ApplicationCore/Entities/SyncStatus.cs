namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// Outcome of a supplier catalog sync. Lets a caller tell — without guessing — whether a sync is
/// still running, finished having captured the supplier's whole listing, or finished having
/// captured only part of it.
/// </summary>
public enum SyncStatus
{
    /// <summary>The sync has been started and is still reading/importing the listing.</summary>
    Running = 0,

    /// <summary>The sync finished and every product found on the listing was brought into the catalog.</summary>
    Completed = 1,

    /// <summary>The sync finished but only some of the products found were brought into the catalog.</summary>
    PartiallyCompleted = 2,

    /// <summary>The sync could not read the listing at all and imported nothing.</summary>
    Failed = 3
}
