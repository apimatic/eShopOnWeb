using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

/// <summary>
/// A single run that reads a supplier's product listing and imports the products it finds
/// into the store catalog. Tracks how many products were found versus how many were actually
/// brought into the catalog so the outcome is unambiguous.
/// </summary>
public class CatalogSync : IAggregateRoot
{
    public Guid Id { get; private set; }
    public Guid SupplierId { get; private set; }
    public SyncStatus Status { get; private set; }

    /// <summary>Number of distinct products discovered in the supplier's listing.</summary>
    public int ItemsFound { get; private set; }

    /// <summary>Number of products actually created or updated in the catalog.</summary>
    public int ItemsImported { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Human-readable detail about the outcome (e.g. why the sync failed or was partial).</summary>
    public string? Detail { get; private set; }

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

    public void MarkRunning()
    {
        Status = SyncStatus.Running;
        StartedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Records the final tally and derives the terminal status: every found product imported
    /// means the whole listing was captured; a shortfall means only part of it landed.
    /// </summary>
    public void Complete(int itemsFound, int itemsImported, string? detail = null)
    {
        Guard.Against.Negative(itemsFound, nameof(itemsFound));
        Guard.Against.Negative(itemsImported, nameof(itemsImported));

        ItemsFound = itemsFound;
        ItemsImported = itemsImported;
        Status = itemsImported >= itemsFound ? SyncStatus.Completed : SyncStatus.PartiallyImported;
        Detail = detail;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void Fail(string detail, int itemsFound = 0, int itemsImported = 0)
    {
        ItemsFound = itemsFound;
        ItemsImported = itemsImported;
        Status = SyncStatus.Failed;
        Detail = detail;
        CompletedAt = DateTimeOffset.UtcNow;
    }
}
