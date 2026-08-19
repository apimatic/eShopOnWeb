using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Finds a catalog brand by its exact name (used to resolve or create brands during import).</summary>
public class CatalogBrandByNameSpecification : Specification<CatalogBrand>
{
    public CatalogBrandByNameSpecification(string brand)
    {
        Query.Where(b => b.Brand == brand);
    }
}
