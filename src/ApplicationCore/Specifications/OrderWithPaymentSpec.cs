using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderWithPaymentSpec : Specification<Order>
{
    public OrderWithPaymentSpec(int orderId)
    {
        Query.Where(o => o.Id == orderId)
            .Include(o => o.OrderItems);
    }
}

public class OrdersByBuyerWithPaymentSpec : Specification<Order>
{
    public OrdersByBuyerWithPaymentSpec(string buyerId)
    {
        Query.Where(o => o.BuyerId == buyerId)
            .Include(o => o.OrderItems)
            .OrderByDescending(o => o.OrderDate);
    }
}
