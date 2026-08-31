using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class CatalogItemsByIdsSpec : Specification<CatalogItem>
{
    public CatalogItemsByIdsSpec(IReadOnlyCollection<int> catalogItemIds)
    {
        Query.Where(i => catalogItemIds.Contains(i.Id));
    }
}
