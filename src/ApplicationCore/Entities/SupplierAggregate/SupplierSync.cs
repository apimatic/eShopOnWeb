using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

/// <summary>
/// A single run of importing a supplier's product listing into the catalog. Tracks the
/// outcome the API exposes: <see cref="Status"/>, <see cref="ItemsFound"/> (products the
/// reader discovered on the listing) and <see cref="ItemsImported"/> (products actually
/// matched into the catalog).
/// </summary>
public class SupplierSync : BaseEntity, IAggregateRoot
{
    public int SupplierId { get; private set; }
    public SyncStatus Status { get; private set; }

    /// <summary>Number of products discovered on the supplier's listing.</summary>
    public int ItemsFound { get; private set; }

    /// <summary>Number of discovered products that were imported (created or updated) in the catalog.</summary>
    public int ItemsImported { get; private set; }

    /// <summary>Human-readable explanation for a partial or failed outcome; null when fully completed.</summary>
    public string? StatusDetail { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public SupplierSync(int supplierId)
    {
        SupplierId = Guard.Against.NegativeOrZero(supplierId, nameof(supplierId));
        Status = SyncStatus.Queued;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkRunning()
    {
        Status = SyncStatus.Running;
        StartedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Records the terminal outcome. The sync is <see cref="SyncStatus.Completed"/> only when the
    /// reader captured the whole listing AND every discovered product was imported; otherwise it is
    /// <see cref="SyncStatus.PartiallyCompleted"/>.
    /// </summary>
    public void MarkFinished(int itemsFound, int itemsImported, bool listingFullyCaptured)
    {
        ItemsFound = Guard.Against.Negative(itemsFound, nameof(itemsFound));
        ItemsImported = Guard.Against.Negative(itemsImported, nameof(itemsImported));

        bool complete = listingFullyCaptured && itemsImported >= itemsFound;
        Status = complete ? SyncStatus.Completed : SyncStatus.PartiallyCompleted;
        StatusDetail = complete ? null : BuildPartialDetail(itemsFound, itemsImported, listingFullyCaptured);
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void MarkFailed(string error)
    {
        Status = SyncStatus.Failed;
        StatusDetail = Guard.Against.NullOrWhiteSpace(error, nameof(error));
        CompletedAt = DateTimeOffset.UtcNow;
    }

    private static string BuildPartialDetail(int found, int imported, bool listingFullyCaptured)
    {
        if (!listingFullyCaptured)
        {
            return $"The supplier's listing was only partially read; imported {imported} of {found} product(s) captured so far.";
        }

        return $"Imported {imported} of {found} product(s); the remainder were missing required data (name or price) and were skipped.";
    }
}
