using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// One run of importing a supplier's product listing into the catalog. Tracks its lifecycle and the
/// found-vs-imported counts so a caller can see exactly how many products were found on the listing
/// versus how many were actually brought into the catalog.
/// </summary>
public class CatalogSync : BaseEntity, IAggregateRoot
{
    public int SupplierId { get; private set; }
    public SyncStatus Status { get; private set; }

    /// <summary>How many products were discovered on the supplier's listing.</summary>
    public int ItemsFound { get; private set; }

    /// <summary>How many of those products were actually created or updated in the catalog.</summary>
    public int ItemsImported { get; private set; }

    public string? Error { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public CatalogSync(int supplierId)
    {
        SupplierId = Guard.Against.NegativeOrZero(supplierId, nameof(supplierId));
        Status = SyncStatus.Running;
        StartedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Records a successful (whole or partial) run. Status is <see cref="SyncStatus.Completed"/> when
    /// every found product made it into the catalog, otherwise <see cref="SyncStatus.PartiallyCompleted"/>.
    /// </summary>
    public void MarkCompleted(int itemsFound, int itemsImported)
    {
        ItemsFound = Guard.Against.Negative(itemsFound, nameof(itemsFound));
        ItemsImported = Guard.Against.Negative(itemsImported, nameof(itemsImported));
        Status = itemsImported >= itemsFound ? SyncStatus.Completed : SyncStatus.PartiallyCompleted;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Records that the run could not read the listing at all.</summary>
    public void MarkFailed(string error)
    {
        Error = Guard.Against.NullOrWhiteSpace(error, nameof(error));
        Status = SyncStatus.Failed;
        CompletedAt = DateTimeOffset.UtcNow;
    }
}
