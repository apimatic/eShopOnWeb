using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

/// <summary>
/// A single run of importing a supplier's product listing into the store catalog.
/// Tracks how many products were found in the listing versus how many were actually
/// brought into the catalog, alongside the run's <see cref="SyncStatus"/>.
/// </summary>
public class CatalogSync : BaseEntity, IAggregateRoot
{
    public int SupplierId { get; private set; }
    public SyncStatus Status { get; private set; }

    /// <summary>Number of products discovered in the supplier's listing.</summary>
    public int ItemsFound { get; private set; }

    /// <summary>Number of discovered products that were successfully imported into the catalog.</summary>
    public int ItemsImported { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Identifier of the underlying Firecrawl extraction job, once one has been started.</summary>
    public string? ExternalJobId { get; private set; }

    /// <summary>Populated only when the sync fails.</summary>
    public string? ErrorMessage { get; private set; }

    public CatalogSync(int supplierId)
    {
        Guard.Against.NegativeOrZero(supplierId, nameof(supplierId));
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

    public void SetExternalJob(string externalJobId)
    {
        Guard.Against.NullOrWhiteSpace(externalJobId, nameof(externalJobId));
        ExternalJobId = externalJobId;
    }

    /// <summary>
    /// Records the outcome of a finished sync. The status becomes <see cref="SyncStatus.Completed"/>
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

    public void Fail(string errorMessage)
    {
        Status = SyncStatus.Failed;
        ErrorMessage = errorMessage;
        CompletedAt = DateTimeOffset.UtcNow;
    }
}
