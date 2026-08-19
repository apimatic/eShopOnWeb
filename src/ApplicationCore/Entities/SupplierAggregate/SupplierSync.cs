using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

/// <summary>
/// A single run that reads a supplier's product listing and imports the products it finds
/// into the store's catalog. Tracks how many products were found versus actually imported.
/// </summary>
public class SupplierSync : BaseEntity, IAggregateRoot
{
    public int SupplierId { get; private set; }

    public SyncStatus Status { get; private set; }

    /// <summary>How many products the sync found on the supplier's listing.</summary>
    public int ItemsFound { get; private set; }

    /// <summary>How many of those products were actually brought into the catalog.</summary>
    public int ItemsImported { get; private set; }

    /// <summary>Populated when the sync fails, to help an operator understand what went wrong.</summary>
    public string? ErrorMessage { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public SupplierSync(int supplierId)
    {
        Guard.Against.NegativeOrZero(supplierId, nameof(supplierId));
        SupplierId = supplierId;
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

    /// <summary>
    /// Records the final tally and derives the terminal status: <see cref="SyncStatus.Completed"/>
    /// when every found product was imported, otherwise <see cref="SyncStatus.PartiallyCompleted"/>.
    /// </summary>
    public void Complete(int itemsFound, int itemsImported)
    {
        Guard.Against.Negative(itemsFound, nameof(itemsFound));
        Guard.Against.Negative(itemsImported, nameof(itemsImported));

        ItemsFound = itemsFound;
        ItemsImported = itemsImported;
        Status = itemsImported >= itemsFound ? SyncStatus.Completed : SyncStatus.PartiallyCompleted;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Marks the sync as failed. Any items imported before the failure are still reflected in the counts.
    /// </summary>
    public void Fail(string errorMessage, int itemsFound = 0, int itemsImported = 0)
    {
        Status = SyncStatus.Failed;
        ErrorMessage = errorMessage;
        ItemsFound = itemsFound;
        ItemsImported = itemsImported;
        CompletedAt = DateTimeOffset.UtcNow;
    }
}
