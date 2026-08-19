using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

/// <summary>
/// Links a supplier's own identifier (or URL) for a product to the catalog item it was
/// imported into. This is the matching record that makes a sync idempotent: re-running a sync
/// finds the existing link by <see cref="SupplierId"/> + <see cref="ExternalId"/> and updates
/// the same catalog item instead of creating a duplicate.
/// </summary>
public class SupplierCatalogItem : BaseEntity, IAggregateRoot
{
    public int SupplierId { get; private set; }

    /// <summary>The supplier's own stable identifier for the product — its detail URL or SKU.</summary>
    public string ExternalId { get; private set; }

    public int CatalogItemId { get; private set; }

    public DateTimeOffset LastSyncedAt { get; private set; }

    private SupplierCatalogItem()
    {
        // Required by EF Core.
        ExternalId = string.Empty;
    }

    public SupplierCatalogItem(int supplierId, string externalId, int catalogItemId)
    {
        SupplierId = Guard.Against.NegativeOrZero(supplierId, nameof(supplierId));
        ExternalId = Guard.Against.NullOrWhiteSpace(externalId, nameof(externalId));
        CatalogItemId = Guard.Against.NegativeOrZero(catalogItemId, nameof(catalogItemId));
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Re-points this link at a catalog item (used to heal a dangling link) and stamps the sync time.</summary>
    public void LinkTo(int catalogItemId)
    {
        CatalogItemId = Guard.Against.NegativeOrZero(catalogItemId, nameof(catalogItemId));
        MarkSynced();
    }

    public void MarkSynced()
    {
        LastSyncedAt = DateTimeOffset.UtcNow;
    }
}
