using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class CatalogItemsByIdsSpec : Specification<CatalogItem>
{
    public CatalogItemsByIdsSpec(IReadOnlyList<int> ids)
    {
        Query.Where(c => ids.Contains(c.Id));
    }
}
