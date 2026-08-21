using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrdersWithPaymentActivityInRangeSpec : Specification<Order>
{
    public OrdersWithPaymentActivityInRangeSpec(DateTimeOffset from, DateTimeOffset to)
    {
        Query
            .Where(o =>
                (o.OrderDate >= from && o.OrderDate <= to) ||
                (o.PaidAt != null && o.PaidAt >= from && o.PaidAt <= to) ||
                (o.FulfilledAt != null && o.FulfilledAt >= from && o.FulfilledAt <= to) ||
                (o.CancelledAt != null && o.CancelledAt >= from && o.CancelledAt <= to))
            .Include(o => o.OrderItems)
            .Include(o => o.Refunds);
    }
}
