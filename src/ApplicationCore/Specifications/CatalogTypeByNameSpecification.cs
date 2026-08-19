using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Finds a catalog type by its (case-insensitive) name, used to reuse types across imports.</summary>
public class CatalogTypeByNameSpecification : Specification<CatalogType>, ISingleResultSpecification<CatalogType>
{
    public CatalogTypeByNameSpecification(string type)
    {
        Query.Where(t => t.Type.ToLower() == type.ToLower());
    }
}
