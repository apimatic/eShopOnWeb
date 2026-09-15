using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderByIdWithPaymentSpec : Specification<Order>, ISingleResultSpecification<Order>
{
    public OrderByIdWithPaymentSpec(int orderId)
    {
        Query
            .Where(order => order.Id == orderId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered)
            .Include(o => o.Refunds);
    }
}

public class BuyerOrderByIdWithPaymentSpec : Specification<Order>, ISingleResultSpecification<Order>
{
    public BuyerOrderByIdWithPaymentSpec(int orderId, string buyerId)
    {
        Query
            .Where(order => order.Id == orderId && order.BuyerId == buyerId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered)
            .Include(o => o.Refunds);
    }
}

public class OrdersWithPaymentInRangeSpec : Specification<Order>
{
    public OrdersWithPaymentInRangeSpec(System.DateTimeOffset from, System.DateTimeOffset to)
    {
        Query
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered)
            .Include(o => o.Refunds)
            .Where(o =>
                (o.OrderDate >= from && o.OrderDate <= to) ||
                (o.AuthorizedAt != null && o.AuthorizedAt >= from && o.AuthorizedAt <= to) ||
                (o.FulfilledAt != null && o.FulfilledAt >= from && o.FulfilledAt <= to) ||
                (o.CancelledAt != null && o.CancelledAt >= from && o.CancelledAt <= to));
    }
}
