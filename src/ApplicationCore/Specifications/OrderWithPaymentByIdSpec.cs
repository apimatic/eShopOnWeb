using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Loads a single order for a given buyer with its items, payment and refunds. Scoping by buyer id
/// enforces ownership: one shopper can never load another's order.
/// </summary>
public class OrderWithPaymentByIdSpec : Specification<Order>
{
    public OrderWithPaymentByIdSpec(int orderId, string buyerId)
    {
        Query
            .Where(order => order.Id == orderId && order.BuyerId == buyerId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered)
            .Include(o => o.Payment)
                .ThenInclude(p => p!.Refunds);
    }

    /// <summary>Operator variant: loads any order by id (with payment) regardless of owner.</summary>
    public OrderWithPaymentByIdSpec(int orderId)
    {
        Query
            .Where(order => order.Id == orderId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered)
            .Include(o => o.Payment)
                .ThenInclude(p => p!.Refunds);
    }
}
