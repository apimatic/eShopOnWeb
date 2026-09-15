using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrdersWithPaymentsSpec : Specification<Order>
{
    public OrdersWithPaymentsSpec()
    {
        Query
            .Include(o => o.OrderItems)
            .Include(o => o.Refunds)
            .Where(o => o.Payment != null);
    }
}

public class CustomerOrdersWithPaymentSpec : Specification<Order>
{
    public CustomerOrdersWithPaymentSpec(string buyerId)
    {
        Query
            .Where(o => o.BuyerId == buyerId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered)
            .Include(o => o.Refunds)
            .OrderByDescending(o => o.OrderDate);
    }
}
