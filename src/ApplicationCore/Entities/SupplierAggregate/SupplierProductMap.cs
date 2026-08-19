using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

/// <summary>
/// Maps a supplier's own identifier (or URL) for a product to the catalog item it was
/// imported into. This is the idempotency anchor: on a re-sync, a product with the same
/// <see cref="ExternalId"/> updates the same catalog item instead of creating a duplicate.
/// </summary>
public class SupplierProductMap : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private SupplierProductMap() { }
#pragma warning restore CS8618

    public SupplierProductMap(int supplierId, string externalId, int catalogItemId)
    {
        Guard.Against.NegativeOrZero(supplierId, nameof(supplierId));
        Guard.Against.NullOrWhiteSpace(externalId, nameof(externalId));
        Guard.Against.NegativeOrZero(catalogItemId, nameof(catalogItemId));

        SupplierId = supplierId;
        ExternalId = externalId;
        CatalogItemId = catalogItemId;
    }

    public int SupplierId { get; private set; }

    /// <summary>The supplier's own stable identifier (SKU) or URL for the product.</summary>
    public string ExternalId { get; private set; }

    public int CatalogItemId { get; private set; }
}
