using System.Collections.Generic;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class CatalogItemsByIdsSpec : Specification<CatalogItem>
{
    public CatalogItemsByIdsSpec(IEnumerable<int> ids)
    {
        var idList = new List<int>(ids);
        Query.Where(c => idList.Contains(c.Id));
    }
}
