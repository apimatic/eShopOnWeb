using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Finds a catalog type by its (case-insensitive) type name.</summary>
public sealed class CatalogTypeByNameSpecification : Specification<CatalogType>
{
    public CatalogTypeByNameSpecification(string type)
    {
        Query.Where(t => t.Type.ToLower() == type.ToLower());
    }
}
