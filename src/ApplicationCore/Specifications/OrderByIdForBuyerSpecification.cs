using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A single order by id that also belongs to the given buyer, with items and refunds. Shopper-scoped
/// so one shopper can never see or act on another's order (a wrong buyer yields no match = 404).
/// </summary>
public class OrderByIdForBuyerSpecification : Specification<Order>
{
    public OrderByIdForBuyerSpecification(int orderId, string buyerId)
    {
        Query.Where(o => o.Id == orderId && o.BuyerId == buyerId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered);
        Query.Include(o => o.Refunds);
    }
}
