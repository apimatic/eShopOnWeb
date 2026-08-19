using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Finds the mapping (if any) between a supplier's own identifier for a product and the catalog
/// item it was previously imported into. This is the idempotency lookup used on every re-sync.
/// </summary>
public class SupplierCatalogItemByKeySpecification : Specification<SupplierCatalogItem>
{
    public SupplierCatalogItemByKeySpecification(int supplierId, string externalId)
    {
        Query.Where(m => m.SupplierId == supplierId && m.ExternalId == externalId);
    }
}
