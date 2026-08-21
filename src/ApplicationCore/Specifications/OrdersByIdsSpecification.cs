using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrdersByIdsSpecification : Specification<Order>
{
    public OrdersByIdsSpecification(IEnumerable<int> ids)
    {
        var idList = ids.ToArray();
        Query.Where(o => idList.Contains(o.Id));
    }
}
