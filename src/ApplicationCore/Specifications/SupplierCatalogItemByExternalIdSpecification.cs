using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Finds the mapping between a supplier's product (identified by the supplier's own id/URL) and the
/// catalog item it was imported into.
/// </summary>
public class SupplierCatalogItemByExternalIdSpecification : Specification<SupplierCatalogItem>
{
    public SupplierCatalogItemByExternalIdSpecification(int supplierId, string externalId)
    {
        Query.Where(m => m.SupplierId == supplierId && m.ExternalId == externalId);
    }
}
