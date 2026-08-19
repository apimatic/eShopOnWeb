using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Matches a catalog brand by its (case-insensitive) name, used to reuse brands during import.</summary>
public class CatalogBrandByNameSpecification : Specification<CatalogBrand>
{
    public CatalogBrandByNameSpecification(string brandName)
    {
        Query.Where(brand => brand.Brand.ToLower() == brandName.ToLower());
    }
}
