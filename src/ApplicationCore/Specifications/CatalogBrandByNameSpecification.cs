using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Finds a catalog brand by its (case-insensitive) name.</summary>
public class CatalogBrandByNameSpecification : Specification<CatalogBrand>, ISingleResultSpecification<CatalogBrand>
{
    public CatalogBrandByNameSpecification(string brand)
    {
        Query.Where(b => b.Brand.ToLower() == brand.ToLower());
    }
}
