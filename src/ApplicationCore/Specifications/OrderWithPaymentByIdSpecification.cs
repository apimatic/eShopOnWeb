using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderWithPaymentByIdSpecification : Specification<Order>
{
    public OrderWithPaymentByIdSpecification(int orderId)
    {
        Query
            .Where(order => order.Id == orderId)
            .Include(order => order.OrderItems)
            .ThenInclude(item => item.ItemOrdered)
            .Include(order => order.Refunds);
    }
}
