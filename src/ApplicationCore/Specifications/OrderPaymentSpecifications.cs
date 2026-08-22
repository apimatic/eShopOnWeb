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

public class OrdersWithPaymentInRangeSpecification : Specification<Order>
{
    public OrdersWithPaymentInRangeSpecification(System.DateTimeOffset from, System.DateTimeOffset to)
    {
        Query.Where(o =>
                o.OrderDate >= from && o.OrderDate <= to
                || (o.AuthorizationCreatedAt != null && o.AuthorizationCreatedAt >= from && o.AuthorizationCreatedAt <= to)
                || (o.CapturedAt != null && o.CapturedAt >= from && o.CapturedAt <= to))
            .Include(o => o.Refunds)
            .Include(o => o.OrderItems);
    }
}
