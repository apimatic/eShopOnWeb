using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Finds an existing catalog type by its name, so a supplier sync reuses a type rather than
/// creating a duplicate.
/// </summary>
public class CatalogTypeByNameSpecification : Specification<CatalogType>
{
    public CatalogTypeByNameSpecification(string typeName)
    {
        Query.Where(type => type.Type == typeName);
    }
}
