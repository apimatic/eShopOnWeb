using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierCatalogAggregate;

/// <summary>
/// The durable link between a product on a supplier's listing (identified by the
/// supplier's own identifier or URL, <see cref="ExternalId"/>) and the catalog item
/// it was imported into. This mapping is what makes a re-sync <em>update</em> the same
/// catalog item instead of creating a duplicate.
/// </summary>
public class SupplierProduct : IAggregateRoot
{
    public Guid Id { get; private set; }
    public Guid SupplierId { get; private set; }

    /// <summary>The supplier's own stable identifier or URL for the product.</summary>
    public string ExternalId { get; private set; }

    /// <summary>The catalog item this supplier product was imported into.</summary>
    public int CatalogItemId { get; private set; }

    public DateTimeOffset FirstImportedAt { get; private set; }
    public DateTimeOffset LastSyncedAt { get; private set; }

    public SupplierProduct(Guid supplierId, string externalId, int catalogItemId)
    {
        Guard.Against.Default(supplierId, nameof(supplierId));
        Guard.Against.NullOrWhiteSpace(externalId, nameof(externalId));
        Guard.Against.NegativeOrZero(catalogItemId, nameof(catalogItemId));

        Id = Guid.NewGuid();
        SupplierId = supplierId;
        ExternalId = externalId;
        CatalogItemId = catalogItemId;
        FirstImportedAt = DateTimeOffset.UtcNow;
        LastSyncedAt = FirstImportedAt;
    }

#pragma warning disable CS8618 // Required by Entity Framework
    private SupplierProduct() { }
#pragma warning restore CS8618

    /// <summary>Records that this mapping was touched again by a later sync.</summary>
    public void MarkSynced() => LastSyncedAt = DateTimeOffset.UtcNow;
}
