using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Finds a catalog brand by its (case-insensitive) brand name.</summary>
public sealed class CatalogBrandByNameSpecification : Specification<CatalogBrand>
{
    public CatalogBrandByNameSpecification(string brand)
    {
        Query.Where(b => b.Brand.ToLower() == brand.ToLower());
    }
}
