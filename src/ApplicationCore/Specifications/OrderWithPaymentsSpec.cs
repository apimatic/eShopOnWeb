using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// An order with its items, payment attempts, refunds and the raw provider responses kept for each.
/// </summary>
public class OrderWithPaymentsSpec : Specification<Order>, ISingleResultSpecification<Order>
{
    public OrderWithPaymentsSpec(int orderId)
    {
        Query
            .Where(order => order.Id == orderId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered)
            .Include(o => o.PaymentAttempts)
                .ThenInclude(a => a.ProviderResponses)
            .Include(o => o.Refunds)
                .ThenInclude(r => r.ProviderResponses)
            .AsSplitQuery();
    }
}
