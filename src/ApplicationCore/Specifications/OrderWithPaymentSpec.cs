using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderWithPaymentSpec : Specification<Order>, ISingleResultSpecification<Order>
{
    public OrderWithPaymentSpec(int orderId)
    {
        Query.Where(o => o.Id == orderId)
            .Include(o => o.OrderItems)
            .Include(o => o.Refunds);
    }
}

public class CustomerOrdersWithPaymentSpec : Specification<Order>
{
    public CustomerOrdersWithPaymentSpec(string buyerId)
    {
        Query.Where(o => o.BuyerId == buyerId)
            .Include(o => o.OrderItems)
            .Include(o => o.Refunds)
            .OrderByDescending(o => o.OrderDate);
    }
}
