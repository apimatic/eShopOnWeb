using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Finds a catalog type by its (case-insensitive) name.</summary>
public class CatalogTypeByNameSpecification : Specification<CatalogType>, ISingleResultSpecification<CatalogType>
{
    public CatalogTypeByNameSpecification(string typeName)
    {
        Query.Where(t => t.Type != null && t.Type.ToLower() == typeName.ToLower());
    }
}
