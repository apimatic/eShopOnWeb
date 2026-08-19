using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class CatalogBrandByNameSpecification : Specification<CatalogBrand>
{
    public CatalogBrandByNameSpecification(string brand)
    {
        Query.Where(b => b.Brand == brand);
    }
}
