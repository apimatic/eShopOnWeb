using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Finds the link between a supplier's product (identified by the supplier's own id/URL) and the
/// catalog item it was imported into. Used to make re-syncs idempotent.
/// </summary>
public class SupplierCatalogItemByKeySpecification : Specification<SupplierCatalogItem>, ISingleResultSpecification<SupplierCatalogItem>
{
    public SupplierCatalogItemByKeySpecification(int supplierId, string externalId)
    {
        Query.Where(link => link.SupplierId == supplierId && link.ExternalId == externalId);
    }
}
