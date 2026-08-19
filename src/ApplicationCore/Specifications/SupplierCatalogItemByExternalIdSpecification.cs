using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Finds the link between a supplier's external product identifier and a catalog item, used to
/// match a scraped product against an already-imported one so a re-sync updates it in place.
/// </summary>
public class SupplierCatalogItemByExternalIdSpecification : Specification<SupplierCatalogItem>
{
    public SupplierCatalogItemByExternalIdSpecification(int supplierId, string externalId)
    {
        Query.Where(link => link.SupplierId == supplierId && link.ExternalId == externalId);
    }
}
