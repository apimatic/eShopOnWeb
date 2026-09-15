using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>An order with its items and full PayPal payment state (authorization, capture, refunds).</summary>
public class OrderWithPaymentByIdSpec : Specification<Order>
{
    public OrderWithPaymentByIdSpec(int orderId)
    {
        Query
            .Where(order => order.Id == orderId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered);

        Query
            .Include(o => o.Payment!)
                .ThenInclude(p => p.Refunds);
    }
}
