using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A single order by id, with its items and (owned) payment loaded. The owned <c>Payment</c> and its
/// refunds are loaded automatically with the aggregate.
/// </summary>
public class OrderWithPaymentByIdSpecification : Specification<Order>, ISingleResultSpecification<Order>
{
    public OrderWithPaymentByIdSpecification(int orderId)
    {
        Query.Where(o => o.Id == orderId)
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.ItemOrdered);
    }
}
