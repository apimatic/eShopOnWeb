using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Finds the existing link for a supplier's product, matching either its identifier/URL
/// (<paramref name="externalKey"/>) or, as a safety net, its normalized name
/// (<paramref name="nameKey"/>). This makes a re-sync update the same catalog item instead of
/// creating a duplicate even when the supplier's identifier/URL is missing on a later read.
/// </summary>
public class SupplierCatalogItemByExternalKeySpecification : Specification<SupplierCatalogItem>, ISingleResultSpecification<SupplierCatalogItem>
{
    public SupplierCatalogItemByExternalKeySpecification(int supplierId, string externalKey, string nameKey)
    {
        Query.Where(link => link.SupplierId == supplierId &&
                            (link.ExternalKey == externalKey || link.NameKey == nameKey));
    }
}
