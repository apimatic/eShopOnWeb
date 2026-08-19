using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Finds a catalog type by its exact name (used to resolve or create the import type).</summary>
public class CatalogTypeByNameSpecification : Specification<CatalogType>
{
    public CatalogTypeByNameSpecification(string type)
    {
        Query.Where(t => t.Type == type);
    }
}
