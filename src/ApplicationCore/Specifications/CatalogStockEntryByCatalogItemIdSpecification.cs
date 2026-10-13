using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class CatalogStockEntryByCatalogItemIdSpecification : Specification<CatalogStockEntry>
{
    public CatalogStockEntryByCatalogItemIdSpecification(int catalogItemId)
    {
        Query.Where(s => s.CatalogItemId == catalogItemId);
    }
}
