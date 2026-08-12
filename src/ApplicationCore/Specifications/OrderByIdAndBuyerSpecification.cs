using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>One order, scoped to its buyer so a shopper only ever sees their own.</summary>
public sealed class OrderByIdAndBuyerSpecification : Specification<Order>
{
    public OrderByIdAndBuyerSpecification(int orderId, string buyerId)
    {
        Query
            .Where(order => order.Id == orderId && order.BuyerId == buyerId)
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.ItemOrdered);
    }
}
