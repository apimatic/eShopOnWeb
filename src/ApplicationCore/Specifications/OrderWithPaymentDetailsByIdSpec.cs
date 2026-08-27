using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderWithPaymentDetailsByIdSpec : Specification<Order>
{
    public OrderWithPaymentDetailsByIdSpec(int orderId)
    {
        Query
            .Where(order => order.Id == orderId)
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.ItemOrdered)
            .Include(o => o.PaymentRefunds);
    }
}
