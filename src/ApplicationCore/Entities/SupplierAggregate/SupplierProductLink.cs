using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

/// <summary>
/// Links a supplier's own identifier for a product (its SKU or product URL) to the catalog
/// item created from it. This is what makes a re-sync idempotent: a product already imported
/// is matched by <see cref="ExternalId"/> and the existing catalog item is updated instead of
/// a duplicate being created.
/// </summary>
public class SupplierProductLink : IAggregateRoot
{
    public Guid Id { get; private set; }
    public Guid SupplierId { get; private set; }

    /// <summary>The supplier's own stable key for the product (SKU, else product URL, else name).</summary>
    public string ExternalId { get; private set; }

    /// <summary>The catalog item this supplier product maps to.</summary>
    public int CatalogItemId { get; private set; }

    public DateTimeOffset FirstImportedAt { get; private set; }
    public DateTimeOffset LastSyncedAt { get; private set; }

    public SupplierProductLink(Guid supplierId, string externalId, int catalogItemId)
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
    private SupplierProductLink() { }
#pragma warning restore CS8618

    public void Touch() => LastSyncedAt = DateTimeOffset.UtcNow;
}
