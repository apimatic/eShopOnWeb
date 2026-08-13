using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>One order with its items, but only if it belongs to the given shopper.</summary>
public class OrderByIdAndBuyerSpecification : Specification<Order>
{
    public OrderByIdAndBuyerSpecification(int orderId, string buyerId)
    {
        Query.Where(o => o.Id == orderId && o.BuyerId == buyerId)
             .Include(o => o.OrderItems)
             .ThenInclude(i => i.ItemOrdered);
    }
}
