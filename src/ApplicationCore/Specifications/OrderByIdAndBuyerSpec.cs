using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>A single order that belongs to the given buyer, with its payment, refunds and items.</summary>
public class OrderByIdAndBuyerSpec : Specification<Order>, ISingleResultSpecification<Order>
{
    public OrderByIdAndBuyerSpec(int orderId, string buyerId)
    {
        Query
            .Where(order => order.Id == orderId && order.BuyerId == buyerId)
            .Include(o => o.Payment)
                .ThenInclude(p => p!.Refunds)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered);
    }
}
