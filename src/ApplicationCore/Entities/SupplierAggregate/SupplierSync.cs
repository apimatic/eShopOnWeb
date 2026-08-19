using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

/// <summary>
/// A single run that reads a supplier's product listing and imports the products it finds
/// into the store's catalog. Records both how many products were found in the listing and how
/// many were actually brought into the catalog, so a caller can tell a full capture from a
/// partial one without guessing.
/// </summary>
public class SupplierSync : BaseEntity, IAggregateRoot
{
    public int SupplierId { get; private set; }
    public SupplierSyncStatus Status { get; private set; }

    /// <summary>Number of products discovered in the supplier's listing.</summary>
    public int ItemsFound { get; private set; }

    /// <summary>Number of products actually imported (created or updated) into the catalog.</summary>
    public int ItemsImported { get; private set; }

    public string? Error { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    private SupplierSync()
    {
        // Required by EF Core.
    }

    public SupplierSync(int supplierId)
    {
        SupplierId = Guard.Against.NegativeOrZero(supplierId, nameof(supplierId));
        Status = SupplierSyncStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkRunning()
    {
        Status = SupplierSyncStatus.Running;
        StartedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Marks the sync finished. The outcome is <see cref="SupplierSyncStatus.Completed"/> when
    /// every found product was imported, otherwise <see cref="SupplierSyncStatus.PartiallyCompleted"/>.
    /// </summary>
    public void MarkCompleted(int itemsFound, int itemsImported)
    {
        ItemsFound = Guard.Against.Negative(itemsFound, nameof(itemsFound));
        ItemsImported = Guard.Against.Negative(itemsImported, nameof(itemsImported));
        Status = itemsImported >= itemsFound
            ? SupplierSyncStatus.Completed
            : SupplierSyncStatus.PartiallyCompleted;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Marks the sync failed because the supplier's listing could not be read.
    /// </summary>
    public void MarkFailed(string error)
    {
        Status = SupplierSyncStatus.Failed;
        Error = error;
        CompletedAt = DateTimeOffset.UtcNow;
    }
}
