using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// One order with its items, payment and refunds. Optionally scoped to a buyer so a shopper-facing
/// request can never load another shopper's order.
/// </summary>
public class OrderWithPaymentByIdSpec : Specification<Order>
{
    public OrderWithPaymentByIdSpec(int orderId, string? buyerId = null)
    {
        Query
            .Where(order => order.Id == orderId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered)
            .Include(o => o.Payment)
                .ThenInclude(p => p!.Refunds);

        if (buyerId != null)
        {
            Query.Where(order => order.BuyerId == buyerId);
        }
    }
}
