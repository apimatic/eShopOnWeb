using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

/// <summary>
/// Links a product on a supplier's listing (identified by the supplier's own identifier or URL
/// for it) to the <see cref="CatalogItem"/> it was imported as. This is what makes a sync
/// idempotent: a product that has already been imported is matched here and updated in place
/// instead of being added to the catalog a second time.
/// </summary>
public class SupplierCatalogItem : BaseEntity, IAggregateRoot
{
    public int SupplierId { get; private set; }

    /// <summary>The supplier's own identifier (SKU) or listing URL for the product.</summary>
    public string ExternalId { get; private set; }

    /// <summary>The store <see cref="CatalogItem"/> this supplier product was imported as.</summary>
    public int CatalogItemId { get; private set; }

    public DateTimeOffset FirstImportedAt { get; private set; }
    public DateTimeOffset LastSyncedAt { get; private set; }

#pragma warning disable CS8618 // Required by Entity Framework
    private SupplierCatalogItem() { }
#pragma warning restore CS8618

    public SupplierCatalogItem(int supplierId, string externalId, int catalogItemId)
    {
        SupplierId = Guard.Against.NegativeOrZero(supplierId, nameof(supplierId));
        ExternalId = Guard.Against.NullOrWhiteSpace(externalId, nameof(externalId));
        CatalogItemId = Guard.Against.NegativeOrZero(catalogItemId, nameof(catalogItemId));
        FirstImportedAt = DateTimeOffset.UtcNow;
        LastSyncedAt = FirstImportedAt;
    }

    public void MarkSynced() => LastSyncedAt = DateTimeOffset.UtcNow;
}
