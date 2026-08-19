using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Finds the catalog item previously imported for a given supplier and supplier item key,
/// so a re-run of a sync updates that item instead of creating a duplicate.
/// </summary>
public sealed class CatalogItemBySupplierItemKeySpecification : Specification<CatalogItem>
{
    public CatalogItemBySupplierItemKeySpecification(int supplierId, string supplierItemKey)
    {
        Query.Where(item => item.SupplierId == supplierId && item.SupplierItemKey == supplierItemKey);
    }
}
