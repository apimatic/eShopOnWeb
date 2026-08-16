using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// An order by id, but only if it belongs to the given buyer. Used to scope shopper actions so one
/// shopper can never see or act on another's order.
/// </summary>
public class OrderWithPaymentForBuyerSpec : Specification<Order>
{
    public OrderWithPaymentForBuyerSpec(int orderId, string buyerId)
    {
        Query
            .Where(o => o.Id == orderId && o.BuyerId == buyerId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered);
        Query
            .Include(o => o.Payment)
                .ThenInclude(p => p!.Refunds);
    }
}
