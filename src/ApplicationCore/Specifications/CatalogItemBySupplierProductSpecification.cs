using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Matches the single catalog item previously imported for a given supplier product,
/// identified by the supplier's own product code. Used to make sync imports idempotent.
/// </summary>
public class CatalogItemBySupplierProductSpecification : Specification<CatalogItem>, ISingleResultSpecification<CatalogItem>
{
    public CatalogItemBySupplierProductSpecification(int supplierId, string supplierProductCode)
    {
        Query.Where(item => item.SupplierId == supplierId &&
                            item.SupplierProductCode == supplierProductCode);
    }
}
