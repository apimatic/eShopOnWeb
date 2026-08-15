using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Loads a single order with its items and full payment state (hold, capture and refunds).
/// Optionally scoped to a buyer so a shopper can only act on their own order.
/// </summary>
public class OrderWithPaymentByIdSpec : Specification<Order>
{
    public OrderWithPaymentByIdSpec(int orderId, string? buyerId = null)
    {
        Query
            .Where(order => order.Id == orderId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered);

        Query
            .Include(o => o.Payment)
                .ThenInclude(p => p!.Refunds);

        if (buyerId is not null)
        {
            Query.Where(order => order.BuyerId == buyerId);
        }
    }
}
