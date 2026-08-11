using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A single order by id, scoped to its owner so one shopper can never see or act on another's.
/// The owned <see cref="OrderPayment"/> (and its refunds) load automatically with the order.
/// </summary>
public class OrderByIdForBuyerSpecification : Specification<Order>
{
    public OrderByIdForBuyerSpecification(int orderId, string buyerId)
    {
        Query
            .Where(o => o.Id == orderId && o.BuyerId == buyerId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered);
    }
}
