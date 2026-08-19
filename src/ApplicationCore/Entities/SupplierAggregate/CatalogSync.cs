using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

/// <summary>
/// A single run of a supplier catalog sync. Tracks its status and the outcome counts
/// (how many products were found on the listing versus how many were actually imported).
/// </summary>
public class CatalogSync : BaseEntity, IAggregateRoot
{
    public int SupplierId { get; private set; }
    public CatalogSyncStatus Status { get; private set; }

    /// <summary>Number of products discovered on the supplier's listing.</summary>
    public int ItemsFound { get; private set; }

    /// <summary>Number of products actually created or updated in the store catalog.</summary>
    public int ItemsImported { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Human-readable note about the outcome (e.g. why a sync was partial or failed).</summary>
    public string? Detail { get; private set; }

#pragma warning disable CS8618 // Required by Entity Framework
    private CatalogSync() { }
#pragma warning restore CS8618

    public CatalogSync(int supplierId)
    {
        SupplierId = Guard.Against.NegativeOrZero(supplierId, nameof(supplierId));
        Status = CatalogSyncStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkRunning()
    {
        Status = CatalogSyncStatus.Running;
        StartedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Records a terminal outcome for the sync.</summary>
    public void Complete(CatalogSyncStatus finalStatus, int itemsFound, int itemsImported, string? detail = null)
    {
        if (finalStatus is not (CatalogSyncStatus.Completed
            or CatalogSyncStatus.PartiallyCompleted
            or CatalogSyncStatus.Failed))
        {
            throw new ArgumentException($"'{finalStatus}' is not a terminal status.", nameof(finalStatus));
        }

        Status = finalStatus;
        ItemsFound = Guard.Against.Negative(itemsFound, nameof(itemsFound));
        ItemsImported = Guard.Against.Negative(itemsImported, nameof(itemsImported));
        Detail = detail;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void Fail(string detail) => Complete(CatalogSyncStatus.Failed, ItemsFound, ItemsImported, detail);
}
