using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

/// <summary>
/// Links a product in a supplier's listing (identified by the supplier's own identifier or URL
/// for it) to the <see cref="CatalogItem"/> it was imported into. This is what makes a re-sync
/// idempotent: a product already imported is matched back to the same catalog item and updated
/// in place rather than duplicated.
/// </summary>
public class SupplierCatalogItem : BaseEntity, IAggregateRoot
{
    public int SupplierId { get; private set; }

    /// <summary>The supplier's own stable identifier for the product (its product URL or SKU).</summary>
    public string ExternalId { get; private set; }

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

#pragma warning disable CS8618 // Required by Entity Framework
    private SupplierCatalogItem() { }
#pragma warning restore CS8618

    public void MarkResynced() => LastSyncedAt = DateTimeOffset.UtcNow;
}
