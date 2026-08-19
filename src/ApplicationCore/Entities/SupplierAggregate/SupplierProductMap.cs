using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

/// <summary>
/// Ties a product on a supplier's listing to the catalog item created from it, keyed by the
/// supplier's own identifier or URL for that product. This is what makes a re-sync update the same
/// catalog item instead of creating a duplicate.
/// </summary>
public class SupplierProductMap : BaseEntity, IAggregateRoot
{
    public int SupplierId { get; private set; }

    /// <summary>The supplier's own stable identifier for the product (its SKU, or its product URL).</summary>
    public string ExternalId { get; private set; }

    public int CatalogItemId { get; private set; }

    public SupplierProductMap(int supplierId, string externalId, int catalogItemId)
    {
        SupplierId = Guard.Against.NegativeOrZero(supplierId, nameof(supplierId));
        ExternalId = Guard.Against.NullOrWhiteSpace(externalId, nameof(externalId));
        CatalogItemId = Guard.Against.NegativeOrZero(catalogItemId, nameof(catalogItemId));
    }
}
