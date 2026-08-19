using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

/// <summary>
/// A single run that reads a supplier's product listing and imports it into the catalog.
/// Carries the outcome a caller needs: the <see cref="Status"/>, how many products were found
/// in the listing (<see cref="ItemsFound"/>) and how many were actually brought into the catalog
/// (<see cref="ItemsImported"/>).
/// </summary>
public class SupplierSync : BaseEntity, IAggregateRoot
{
    public int SupplierId { get; private set; }
    public SyncStatus Status { get; private set; }
    public int ItemsFound { get; private set; }
    public int ItemsImported { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? Error { get; private set; }

    public SupplierSync(int supplierId)
    {
        SupplierId = Guard.Against.NegativeOrZero(supplierId, nameof(supplierId));
        Status = SyncStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
    }

#pragma warning disable CS8618 // Required by Entity Framework
    private SupplierSync() { }
#pragma warning restore CS8618

    public void MarkRunning()
    {
        Status = SyncStatus.Running;
        StartedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Records how many products the listing yielded and how many landed in the catalog.</summary>
    public void RecordCounts(int itemsFound, int itemsImported)
    {
        ItemsFound = Guard.Against.Negative(itemsFound, nameof(itemsFound));
        ItemsImported = Guard.Against.Negative(itemsImported, nameof(itemsImported));
    }

    /// <summary>
    /// Marks the sync finished. It counts as fully <see cref="SyncStatus.Completed"/> only when the
    /// reader captured the supplier's whole listing (<paramref name="listingFullyCaptured"/>) and every
    /// found product was imported; otherwise it is <see cref="SyncStatus.PartiallyCompleted"/>.
    /// </summary>
    public void MarkFinished(bool listingFullyCaptured)
    {
        Status = (listingFullyCaptured && ItemsImported == ItemsFound)
            ? SyncStatus.Completed
            : SyncStatus.PartiallyCompleted;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void MarkFailed(string error)
    {
        Status = SyncStatus.Failed;
        Error = error;
        CompletedAt = DateTimeOffset.UtcNow;
    }
}
