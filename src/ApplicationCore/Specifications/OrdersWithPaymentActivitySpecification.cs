using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrdersWithPaymentActivitySpecification : Specification<Order>
{
    public OrdersWithPaymentActivitySpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(o =>
                (o.OrderDate >= from && o.OrderDate <= to) ||
                (o.AuthorizedAt != null && o.AuthorizedAt >= from && o.AuthorizedAt <= to) ||
                (o.CapturedAt != null && o.CapturedAt >= from && o.CapturedAt <= to) ||
                (o.CancelledAt != null && o.CancelledAt >= from && o.CancelledAt <= to))
            .Include(o => o.OrderItems)
            .Include(o => o.Refunds);
    }
}
