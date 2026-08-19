using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Finds the catalog item previously imported for a given supplier product, so a re-sync updates
/// it in place rather than creating a duplicate.
/// </summary>
public class CatalogItemBySupplierKeySpecification : Specification<CatalogItem>
{
    public CatalogItemBySupplierKeySpecification(int supplierId, string supplierProductKey)
    {
        Query.Where(item => item.SupplierId == supplierId && item.SupplierProductKey == supplierProductKey);
    }
}
