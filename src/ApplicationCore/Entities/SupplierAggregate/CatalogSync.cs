using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

/// <summary>
/// One run of importing a supplier's product listing into the store's catalog.
/// Tracks how many products were found in the listing versus how many were actually
/// brought into the catalog, so partial imports are distinguishable from complete ones.
/// </summary>
public class CatalogSync : BaseEntity, IAggregateRoot
{
    public int SupplierId { get; private set; }

    public SupplierSyncStatus Status { get; private set; }

    /// <summary>Number of products discovered in the supplier's listing.</summary>
    public int ItemsFound { get; private set; }

    /// <summary>Number of products actually created or updated in the catalog.</summary>
    public int ItemsImported { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Populated when <see cref="Status"/> is <see cref="SupplierSyncStatus.Failed"/>.</summary>
    public string? ErrorMessage { get; private set; }

    public CatalogSync(int supplierId)
    {
        SupplierId = Guard.Against.NegativeOrZero(supplierId, nameof(supplierId));
        Status = SupplierSyncStatus.Pending;
        RequestedAt = DateTimeOffset.UtcNow;
    }

#pragma warning disable CS8618 // Required by Entity Framework
    private CatalogSync() { }
#pragma warning restore CS8618

    public void MarkRunning()
    {
        Status = SupplierSyncStatus.Running;
        StartedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Records the outcome of a listing read. The status is derived from the counts:
    /// a complete import when every found product was imported, otherwise a partial one.
    /// </summary>
    public void MarkCompleted(int itemsFound, int itemsImported)
    {
        Guard.Against.Negative(itemsFound, nameof(itemsFound));
        Guard.Against.Negative(itemsImported, nameof(itemsImported));

        ItemsFound = itemsFound;
        ItemsImported = itemsImported;
        Status = itemsImported >= itemsFound
            ? SupplierSyncStatus.Completed
            : SupplierSyncStatus.PartiallyCompleted;
        ErrorMessage = null;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void MarkFailed(string errorMessage, int itemsFound = 0, int itemsImported = 0)
    {
        ItemsFound = itemsFound;
        ItemsImported = itemsImported;
        Status = SupplierSyncStatus.Failed;
        ErrorMessage = Guard.Against.NullOrWhiteSpace(errorMessage, nameof(errorMessage));
        CompletedAt = DateTimeOffset.UtcNow;
    }
}
