using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Loads a single order with its payment, refunds and items — enough to act on the payment.</summary>
public class OrderWithPaymentSpecification : Specification<Order>
{
    public OrderWithPaymentSpecification(int orderId)
    {
        Query.Where(o => o.Id == orderId)
            .Include(o => o.Payment)
                .ThenInclude(p => p!.Refunds)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered);
    }
}
