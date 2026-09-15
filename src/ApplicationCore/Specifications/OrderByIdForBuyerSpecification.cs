using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderByIdForBuyerSpecification : Specification<Order>
{
    public OrderByIdForBuyerSpecification(int orderId, string buyerId)
    {
        Query.Where(o => o.Id == orderId && o.BuyerId == buyerId);
    }
}
