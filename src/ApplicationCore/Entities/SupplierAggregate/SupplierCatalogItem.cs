using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

/// <summary>
/// Links a product in a supplier's listing to the catalog item it was imported into, keyed by the
/// supplier's own identifier or URL for that product. This mapping is what makes a re-sync update
/// the same catalog item instead of creating a duplicate.
/// </summary>
public class SupplierCatalogItem : BaseEntity, IAggregateRoot
{
    public int SupplierId { get; private set; }

    /// <summary>The supplier's own stable identifier for the product (its SKU or product URL).</summary>
    public string ExternalId { get; private set; }

    public int CatalogItemId { get; private set; }
    public DateTimeOffset LastSyncedDate { get; private set; }

    public SupplierCatalogItem(int supplierId, string externalId, int catalogItemId)
    {
        SupplierId = Guard.Against.NegativeOrZero(supplierId, nameof(supplierId));
        ExternalId = Guard.Against.NullOrWhiteSpace(externalId, nameof(externalId));
        CatalogItemId = Guard.Against.NegativeOrZero(catalogItemId, nameof(catalogItemId));
        LastSyncedDate = DateTimeOffset.UtcNow;
    }

    public void MarkSynced() => LastSyncedDate = DateTimeOffset.UtcNow;
}
