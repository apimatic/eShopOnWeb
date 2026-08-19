using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

/// <summary>
/// A single run that reads a supplier's product listing and matches every product it finds
/// into the store's own catalog. Tracks how many products were found on the listing versus
/// how many were actually brought into the catalog.
/// </summary>
public class CatalogSync : BaseEntity, IAggregateRoot
{
    public int SupplierId { get; private set; }
    public SyncStatus Status { get; private set; }

    /// <summary>Number of products discovered on the supplier's listing.</summary>
    public int ItemsFound { get; private set; }

    /// <summary>Number of products actually created or updated in the catalog.</summary>
    public int ItemsImported { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? ErrorMessage { get; private set; }

    public CatalogSync(int supplierId)
    {
        Guard.Against.NegativeOrZero(supplierId, nameof(supplierId));

        SupplierId = supplierId;
        Status = SyncStatus.Running;
        StartedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Records a successful run. The sync is <see cref="SyncStatus.Completed"/> only when every
    /// product found was imported; otherwise it is <see cref="SyncStatus.Partial"/>.
    /// </summary>
    public void MarkFinished(int itemsFound, int itemsImported)
    {
        Guard.Against.Negative(itemsFound, nameof(itemsFound));
        Guard.Against.Negative(itemsImported, nameof(itemsImported));

        ItemsFound = itemsFound;
        ItemsImported = itemsImported;
        Status = itemsImported >= itemsFound ? SyncStatus.Completed : SyncStatus.Partial;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Records that the listing could not be read at all. Any items imported before the failure
    /// (if partial progress was possible) are preserved in the counts.
    /// </summary>
    public void MarkFailed(string errorMessage, int itemsFound = 0, int itemsImported = 0)
    {
        ItemsFound = itemsFound;
        ItemsImported = itemsImported;
        Status = SyncStatus.Failed;
        ErrorMessage = errorMessage;
        CompletedAt = DateTimeOffset.UtcNow;
    }
}
