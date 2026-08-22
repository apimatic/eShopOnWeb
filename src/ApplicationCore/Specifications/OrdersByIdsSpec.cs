using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrdersByIdsSpec : Specification<Order>
{
    public OrdersByIdsSpec(int[] orderIds)
    {
        Query.Where(o => orderIds.Contains(o.Id))
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.ItemOrdered);
    }
}
