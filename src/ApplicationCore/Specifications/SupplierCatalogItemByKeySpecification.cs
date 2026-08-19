using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Finds the mapping (if any) between a supplier's product identifier and an imported catalog
/// item. Used on every sync to decide create-vs-update, which keeps re-syncs idempotent.
/// </summary>
public class SupplierCatalogItemByKeySpecification : Specification<SupplierCatalogItem>
{
    public SupplierCatalogItemByKeySpecification(int supplierId, string externalId)
    {
        Query.Where(m => m.SupplierId == supplierId && m.ExternalId == externalId);
    }
}
