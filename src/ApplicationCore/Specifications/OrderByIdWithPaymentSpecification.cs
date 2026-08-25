using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public sealed class OrderByIdWithPaymentSpecification : Specification<Order>
{
    public OrderByIdWithPaymentSpecification(int orderId)
    {
        Query
            .Where(o => o.Id == orderId)
            .Include(o => o.OrderItems)
            .Include(o => o.Payment!)
                .ThenInclude(p => p.Refunds);
    }
}
