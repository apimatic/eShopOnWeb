using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderWithPaymentByIdSpec : Specification<Order>, ISingleResultSpecification<Order>
{
    public OrderWithPaymentByIdSpec(int orderId)
    {
        Query
            .Where(order => order.Id == orderId)
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.ItemOrdered)
            .Include(o => o.Refunds);
    }
}

public class CustomerOrdersWithPaymentSpecification : Specification<Order>
{
    public CustomerOrdersWithPaymentSpecification(string buyerId)
    {
        Query.Where(o => o.BuyerId == buyerId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered)
            .Include(o => o.Refunds)
            .OrderByDescending(o => o.OrderDate);
    }
}

public class OrdersWithPaymentActivitySpec : Specification<Order>
{
    public OrdersWithPaymentActivitySpec()
    {
        Query
            .Where(o => o.PayPalOrderId != null || o.AuthorizationId != null || o.CaptureId != null)
            .Include(o => o.Refunds);
    }
}
