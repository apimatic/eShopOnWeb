using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Matches the catalog item previously imported for a given supplier product, identified by the
/// supplier and the supplier's own stable key (URL/identifier) for that product.
/// </summary>
public sealed class CatalogItemsBySupplierKeySpecification : Specification<CatalogItem>
{
    public CatalogItemsBySupplierKeySpecification(int supplierId, string supplierProductKey)
    {
        Query.Where(i => i.SupplierId == supplierId && i.SupplierProductKey == supplierProductKey);
    }
}
