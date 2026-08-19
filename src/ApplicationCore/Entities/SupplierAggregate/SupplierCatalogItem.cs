using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

/// <summary>
/// Links a product on a supplier's listing to the catalog item it was imported into, so re-running
/// a sync updates the same catalog item instead of creating a duplicate.
/// <para>
/// Matching is primarily by <see cref="ExternalKey"/> - the supplier's own identifier or URL for the
/// product. <see cref="NameKey"/> is a secondary, always-present key on the product name: because a
/// scraped listing does not always expose an id/URL for every product on every read, the name key
/// guarantees the same product is never imported twice even if its identifier/URL is missing on a
/// later read.
/// </para>
/// </summary>
public class SupplierCatalogItem : BaseEntity, IAggregateRoot
{
    public int SupplierId { get; private set; }

    /// <summary>The supplier's own identifier or URL for the product (its natural key on the listing).</summary>
    public string ExternalKey { get; private set; }

    /// <summary>Normalized product name; a stable secondary key that is always present.</summary>
    public string NameKey { get; private set; }

    public int CatalogItemId { get; private set; }

    public SupplierCatalogItem(int supplierId, string externalKey, string nameKey, int catalogItemId)
    {
        Guard.Against.NegativeOrZero(supplierId, nameof(supplierId));
        Guard.Against.NullOrWhiteSpace(externalKey, nameof(externalKey));
        Guard.Against.NullOrWhiteSpace(nameKey, nameof(nameKey));
        Guard.Against.NegativeOrZero(catalogItemId, nameof(catalogItemId));

        SupplierId = supplierId;
        ExternalKey = externalKey;
        NameKey = nameKey;
        CatalogItemId = catalogItemId;
    }

    /// <summary>Refreshes the stored keys so a matched link self-heals if the identifier/URL drifts between reads.</summary>
    public void UpdateKeys(string externalKey, string nameKey)
    {
        Guard.Against.NullOrWhiteSpace(externalKey, nameof(externalKey));
        Guard.Against.NullOrWhiteSpace(nameKey, nameof(nameKey));

        ExternalKey = externalKey;
        NameKey = nameKey;
    }

    public void PointToCatalogItem(int catalogItemId)
    {
        Guard.Against.NegativeOrZero(catalogItemId, nameof(catalogItemId));
        CatalogItemId = catalogItemId;
    }
}
