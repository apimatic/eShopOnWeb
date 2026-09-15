using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrdersWithPaymentByDateRangeSpec : Specification<Order>
{
    public OrdersWithPaymentByDateRangeSpec(System.DateTimeOffset from, System.DateTimeOffset to)
    {
        Query.Where(o => o.OrderDate >= from && o.OrderDate <= to)
            .Include(o => o.OrderItems)
            .Include(o => o.Payment)
                .ThenInclude(p => p!.Refunds);
    }
}
