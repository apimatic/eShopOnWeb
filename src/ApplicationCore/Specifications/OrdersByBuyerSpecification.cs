using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrdersByBuyerSpecification : Specification<Order>
{
    public OrdersByBuyerSpecification(string buyerId)
    {
        Query
            .Where(order => order.BuyerId == buyerId)
            .Include(order => order.OrderItems)
            .ThenInclude(item => item.ItemOrdered)
            .Include(order => order.Refunds)
            .OrderByDescending(order => order.OrderDate);
    }
}
