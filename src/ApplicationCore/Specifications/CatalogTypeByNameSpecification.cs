using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class CatalogTypeByNameSpecification : Specification<CatalogType>
{
    public CatalogTypeByNameSpecification(string type)
    {
        Query.Where(t => t.Type == type);
    }
}
