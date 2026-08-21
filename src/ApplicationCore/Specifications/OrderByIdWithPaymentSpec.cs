using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>A single order by id (any buyer), with its payment and refunds — for operator actions.</summary>
public class OrderByIdWithPaymentSpec : Specification<Order>, ISingleResultSpecification<Order>
{
    public OrderByIdWithPaymentSpec(int orderId)
    {
        Query
            .Where(order => order.Id == orderId)
            .Include(o => o.Payment)
                .ThenInclude(p => p!.Refunds)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered);
    }
}
