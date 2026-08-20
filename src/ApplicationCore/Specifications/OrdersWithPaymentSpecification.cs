using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrdersWithPaymentSpecification : Specification<Order>
{
    public OrdersWithPaymentSpecification()
    {
        Query.Where(o => o.Payment != null)
            .Include(o => o.Payment)
                .ThenInclude(p => p!.Refunds)
            .Include(o => o.OrderItems);
    }
}
