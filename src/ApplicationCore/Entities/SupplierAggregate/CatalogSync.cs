using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

/// <summary>
/// A single run of a supplier catalog sync. Tracks its lifecycle and outcome:
/// how many products were found in the supplier's listing versus how many were
/// actually brought into the store's catalog.
/// </summary>
public class CatalogSync : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private CatalogSync() { }
#pragma warning restore CS8618

    public CatalogSync(int supplierId)
    {
        Guard.Against.NegativeOrZero(supplierId, nameof(supplierId));
        SupplierId = supplierId;
        Status = CatalogSyncStatus.Running;
        StartedAt = DateTimeOffset.UtcNow;
    }

    public int SupplierId { get; private set; }

    public CatalogSyncStatus Status { get; private set; }

    /// <summary>Number of products discovered in the supplier's listing.</summary>
    public int ItemsFound { get; private set; }

    /// <summary>Number of products actually brought into the catalog (created or updated).</summary>
    public int ItemsImported { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Populated when <see cref="Status"/> is <see cref="CatalogSyncStatus.Failed"/>.</summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>
    /// Records a successful (fully or partially) run. The sync is <see cref="CatalogSyncStatus.Completed"/>
    /// only when the whole listing was read (<paramref name="listingFullyCaptured"/>) and every found
    /// product was imported; otherwise it is <see cref="CatalogSyncStatus.PartiallyCompleted"/>.
    /// </summary>
    public void MarkFinished(int itemsFound, int itemsImported, bool listingFullyCaptured)
    {
        Guard.Against.Negative(itemsFound, nameof(itemsFound));
        Guard.Against.Negative(itemsImported, nameof(itemsImported));

        ItemsFound = itemsFound;
        ItemsImported = itemsImported;
        Status = (listingFullyCaptured && itemsImported == itemsFound)
            ? CatalogSyncStatus.Completed
            : CatalogSyncStatus.PartiallyCompleted;
        CompletedAt = DateTimeOffset.UtcNow;
        ErrorMessage = null;
    }

    /// <summary>Records that the sync failed before it could capture the listing.</summary>
    public void MarkFailed(string errorMessage)
    {
        Status = CatalogSyncStatus.Failed;
        ErrorMessage = errorMessage;
        CompletedAt = DateTimeOffset.UtcNow;
    }
}
