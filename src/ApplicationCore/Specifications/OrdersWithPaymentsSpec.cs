using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrdersWithPaymentsSpec : Specification<Order>
{
    public OrdersWithPaymentsSpec()
    {
        Query
            .Where(o => o.Status != OrderStatus.AwaitingPayment)
            .Include(o => o.Refunds)
            .Include(o => o.OrderItems);
    }
}
