using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Finds an existing catalog brand by its name, so a supplier sync reuses a brand rather than
/// creating a duplicate.
/// </summary>
public class CatalogBrandByNameSpecification : Specification<CatalogBrand>
{
    public CatalogBrandByNameSpecification(string brandName)
    {
        Query.Where(brand => brand.Brand == brandName);
    }
}
