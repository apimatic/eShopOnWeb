using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

/// <summary>
/// One run of reading a supplier's product listing and importing what it finds into the catalog.
/// Tracks how many products were found on the listing versus how many were actually brought into
/// the catalog, so the outcome (whole vs. partial) is never a guess.
/// </summary>
public class CatalogSync : BaseEntity, IAggregateRoot
{
    public int SupplierId { get; private set; }

    public SyncStatus Status { get; private set; }

    /// <summary>Number of products found on the supplier's listing.</summary>
    public int ItemsFound { get; private set; }

    /// <summary>Number of products actually imported (created or updated) into the catalog.</summary>
    public int ItemsImported { get; private set; }

    /// <summary>The Firecrawl job id backing this sync, kept for traceability.</summary>
    public string? ExternalJobId { get; private set; }

    public string? ErrorMessage { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }
    public DateTimeOffset? StartedDate { get; private set; }
    public DateTimeOffset? CompletedDate { get; private set; }

    public CatalogSync(int supplierId)
    {
        SupplierId = Guard.Against.NegativeOrZero(supplierId, nameof(supplierId));
        Status = SyncStatus.Pending;
        CreatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>Marks the sync as actively reading the listing.</summary>
    public void MarkRunning()
    {
        Status = SyncStatus.Running;
        StartedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>Records the id of the underlying Firecrawl job once it has been started.</summary>
    public void RecordExternalJob(string externalJobId)
    {
        ExternalJobId = Guard.Against.NullOrWhiteSpace(externalJobId, nameof(externalJobId));
    }

    /// <summary>
    /// Completes the sync. The status becomes <see cref="SyncStatus.Completed"/> when every product
    /// found was imported, or <see cref="SyncStatus.PartiallyCompleted"/> when only some were.
    /// </summary>
    public void Complete(int itemsFound, int itemsImported)
    {
        ItemsFound = Guard.Against.Negative(itemsFound, nameof(itemsFound));
        ItemsImported = Guard.Against.Negative(itemsImported, nameof(itemsImported));
        Status = itemsImported >= itemsFound ? SyncStatus.Completed : SyncStatus.PartiallyCompleted;
        CompletedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>Fails the sync, recording why it could not be completed.</summary>
    public void Fail(string errorMessage)
    {
        Status = SyncStatus.Failed;
        ErrorMessage = Guard.Against.NullOrWhiteSpace(errorMessage, nameof(errorMessage));
        CompletedDate = DateTimeOffset.UtcNow;
    }
}
