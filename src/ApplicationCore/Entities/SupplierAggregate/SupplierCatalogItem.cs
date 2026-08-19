using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

/// <summary>
/// Links a supplier's own product identifier (its product URL or id) to the catalog item it
/// was imported as. This mapping is what makes re-syncing idempotent: a product seen again is
/// matched to its existing <see cref="CatalogItemId"/> and updated in place instead of being
/// added a second time. Unique per (SupplierId, ExternalId).
/// </summary>
public class SupplierCatalogItem : BaseEntity, IAggregateRoot
{
    public int SupplierId { get; private set; }

    /// <summary>The supplier's own stable identifier for the product — its product URL or id.</summary>
    public string ExternalId { get; private set; }

    /// <summary>The id of the <c>CatalogItem</c> this supplier product was imported as.</summary>
    public int CatalogItemId { get; private set; }

    public DateTimeOffset FirstImportedAt { get; private set; }
    public DateTimeOffset LastSyncedAt { get; private set; }

    public SupplierCatalogItem(int supplierId, string externalId, int catalogItemId)
    {
        SupplierId = Guard.Against.NegativeOrZero(supplierId, nameof(supplierId));
        ExternalId = Guard.Against.NullOrWhiteSpace(externalId, nameof(externalId));
        CatalogItemId = Guard.Against.NegativeOrZero(catalogItemId, nameof(catalogItemId));
        FirstImportedAt = DateTimeOffset.UtcNow;
        LastSyncedAt = FirstImportedAt;
    }

    public void MarkSynced() => LastSyncedAt = DateTimeOffset.UtcNow;

    /// <summary>Repoints this mapping at a freshly recreated catalog item (recovery path).</summary>
    public void RepointTo(int catalogItemId)
    {
        CatalogItemId = Guard.Against.NegativeOrZero(catalogItemId, nameof(catalogItemId));
        LastSyncedAt = DateTimeOffset.UtcNow;
    }
}
