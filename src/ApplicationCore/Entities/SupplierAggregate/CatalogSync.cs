using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

/// <summary>
/// A single run that reads a supplier's product listing and imports its products into the catalog.
/// Tracks how many products were found versus how many were actually brought into the catalog so a
/// caller can tell a full capture from a partial one.
/// </summary>
public class CatalogSync : BaseEntity, IAggregateRoot
{
    public int SupplierId { get; private set; }
    public SyncStatus Status { get; private set; }

    /// <summary>How many distinct products were discovered in the supplier's listing.</summary>
    public int ItemsFound { get; private set; }

    /// <summary>How many of the found products were actually created or updated in the catalog.</summary>
    public int ItemsImported { get; private set; }

    public DateTimeOffset StartedDate { get; private set; }
    public DateTimeOffset? CompletedDate { get; private set; }
    public string? ErrorMessage { get; private set; }

    public CatalogSync(int supplierId)
    {
        SupplierId = Guard.Against.NegativeOrZero(supplierId, nameof(supplierId));
        Status = SyncStatus.Running;
        StartedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Records the outcome of a run that successfully read the listing. The status is
    /// <see cref="SyncStatus.Completed"/> when every found product was imported and
    /// <see cref="SyncStatus.PartiallyCompleted"/> when some found products could not be imported.
    /// </summary>
    public void Complete(int itemsFound, int itemsImported)
    {
        ItemsFound = Guard.Against.Negative(itemsFound, nameof(itemsFound));
        ItemsImported = Guard.Against.Negative(itemsImported, nameof(itemsImported));
        Status = itemsImported >= itemsFound ? SyncStatus.Completed : SyncStatus.PartiallyCompleted;
        CompletedDate = DateTimeOffset.UtcNow;
        ErrorMessage = null;
    }

    /// <summary>Records that the listing could not be read; nothing was imported.</summary>
    public void Fail(string errorMessage)
    {
        Status = SyncStatus.Failed;
        ErrorMessage = Guard.Against.NullOrWhiteSpace(errorMessage, nameof(errorMessage));
        CompletedDate = DateTimeOffset.UtcNow;
    }
}
