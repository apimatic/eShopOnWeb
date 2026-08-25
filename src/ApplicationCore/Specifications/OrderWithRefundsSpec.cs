using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderWithRefundsSpec : Specification<Order>
{
    public OrderWithRefundsSpec(int orderId)
    {
        Query.Where(o => o.Id == orderId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered)
            .Include(o => o.Refunds);
    }
}

public class CustomerOrdersWithPaymentSpec : Specification<Order>
{
    public CustomerOrdersWithPaymentSpec(string buyerId)
    {
        Query.Where(o => o.BuyerId == buyerId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered)
            .Include(o => o.Refunds);
    }
}

public class AllOrdersWithPaymentSpec : Specification<Order>
{
    public AllOrdersWithPaymentSpec()
    {
        Query.Include(o => o.Refunds);
    }
}
