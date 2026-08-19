using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Finds the link (if any) between a supplier product and the catalog item it was imported as,
/// keyed by the supplier and the supplier's own identifier/URL for the product.
/// </summary>
public class SupplierCatalogItemSpecification : Specification<SupplierCatalogItem>
{
    public SupplierCatalogItemSpecification(int supplierId, string externalId)
    {
        Query.Where(link => link.SupplierId == supplierId && link.ExternalId == externalId);
    }
}
