using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Matches a catalog type by its (case-insensitive) name, used to reuse types during import.</summary>
public class CatalogTypeByNameSpecification : Specification<CatalogType>
{
    public CatalogTypeByNameSpecification(string typeName)
    {
        Query.Where(type => type.Type.ToLower() == typeName.ToLower());
    }
}
