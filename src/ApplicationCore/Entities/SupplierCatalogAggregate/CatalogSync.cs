using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierCatalogAggregate;

/// <summary>
/// A single run that reads a supplier's product listing and imports its products
/// into the store catalog. Tracks how many products were found on the listing
/// versus how many were actually brought into the catalog.
/// </summary>
public class CatalogSync : IAggregateRoot
{
    public Guid Id { get; private set; }
    public Guid SupplierId { get; private set; }
    public SyncStatus Status { get; private set; }

    /// <summary>How many products were found on the supplier's listing.</summary>
    public int ItemsFound { get; private set; }

    /// <summary>How many of those products were actually imported into the catalog.</summary>
    public int ItemsImported { get; private set; }

    /// <summary>Populated only when <see cref="Status"/> is <see cref="SyncStatus.Failed"/>.</summary>
    public string? Error { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public CatalogSync(Guid supplierId)
    {
        Guard.Against.Default(supplierId, nameof(supplierId));
        Id = Guid.NewGuid();
        SupplierId = supplierId;
        Status = SyncStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
    }

#pragma warning disable CS8618 // Required by Entity Framework
    private CatalogSync() { }
#pragma warning restore CS8618

    /// <summary>Marks the sync as actively running (worker has picked it up).</summary>
    public void MarkRunning()
    {
        Status = SyncStatus.Running;
        StartedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Marks the sync finished. The outcome is derived from the counts: a sync that
    /// imported every product it found is <see cref="SyncStatus.Completed"/>; one that
    /// imported only some of them is <see cref="SyncStatus.PartiallyCompleted"/>.
    /// </summary>
    public void Complete(int itemsFound, int itemsImported)
    {
        Guard.Against.Negative(itemsFound, nameof(itemsFound));
        Guard.Against.Negative(itemsImported, nameof(itemsImported));

        ItemsFound = itemsFound;
        ItemsImported = itemsImported;
        Status = itemsImported >= itemsFound
            ? SyncStatus.Completed
            : SyncStatus.PartiallyCompleted;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Marks the sync as failed, preserving any partial counts captured so far.</summary>
    public void Fail(string error, int itemsFound = 0, int itemsImported = 0)
    {
        ItemsFound = itemsFound;
        ItemsImported = itemsImported;
        Status = SyncStatus.Failed;
        Error = error;
        CompletedAt = DateTimeOffset.UtcNow;
    }
}
