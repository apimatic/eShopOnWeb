using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

/// <summary>
/// One run of syncing a supplier's product listing into the catalog. Tracks the outcome so a
/// caller can tell whether the sync is still running, finished having captured the whole
/// listing, or finished having captured only part of it, plus how many products were found
/// versus actually imported.
/// </summary>
public class SupplierSync : BaseEntity, IAggregateRoot
{
    public int SupplierId { get; private set; }
    public SyncStatus Status { get; private set; }

    /// <summary>Number of products discovered in the supplier's listing.</summary>
    public int ItemsFound { get; private set; }

    /// <summary>Number of products actually created or updated in the catalog.</summary>
    public int ItemsImported { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Set when the sync fails; null otherwise.</summary>
    public string? ErrorMessage { get; private set; }

    public SupplierSync(int supplierId)
    {
        SupplierId = Guard.Against.NegativeOrZero(supplierId, nameof(supplierId));
        Status = SyncStatus.Queued;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkRunning()
    {
        Status = SyncStatus.Running;
        StartedAt = DateTimeOffset.UtcNow;
        ErrorMessage = null;
    }

    /// <summary>
    /// Records a successful (fully or partially) completed run. The run is considered complete
    /// only when every found product was imported <em>and</em> the listing was fully captured;
    /// otherwise it is marked partially completed.
    /// </summary>
    public void MarkCompleted(int itemsFound, int itemsImported, bool listingFullyCaptured)
    {
        ItemsFound = Guard.Against.Negative(itemsFound, nameof(itemsFound));
        ItemsImported = Guard.Against.Negative(itemsImported, nameof(itemsImported));
        Status = (listingFullyCaptured && itemsImported >= itemsFound)
            ? SyncStatus.Completed
            : SyncStatus.PartiallyCompleted;
        CompletedAt = DateTimeOffset.UtcNow;
        ErrorMessage = null;
    }

    public void MarkFailed(string errorMessage, int itemsFound = 0, int itemsImported = 0)
    {
        Status = SyncStatus.Failed;
        ItemsFound = Guard.Against.Negative(itemsFound, nameof(itemsFound));
        ItemsImported = Guard.Against.Negative(itemsImported, nameof(itemsImported));
        ErrorMessage = Guard.Against.NullOrWhiteSpace(errorMessage, nameof(errorMessage));
        CompletedAt = DateTimeOffset.UtcNow;
    }
}
