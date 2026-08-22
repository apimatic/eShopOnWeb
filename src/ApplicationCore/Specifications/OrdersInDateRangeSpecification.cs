using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrdersInDateRangeSpecification : Specification<Order>
{
    public OrdersInDateRangeSpecification(System.DateTimeOffset from, System.DateTimeOffset to)
    {
        Query.Where(o => o.OrderDate >= from && o.OrderDate <= to)
            .Include(o => o.OrderItems)
            .Include(o => o.Refunds);
    }
}
