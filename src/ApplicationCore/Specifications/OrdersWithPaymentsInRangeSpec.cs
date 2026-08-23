using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrdersWithPaymentsInRangeSpec : Specification<Order>
{
    public OrdersWithPaymentsInRangeSpec(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(o =>
                (o.OrderDate >= from && o.OrderDate <= to) ||
                (o.Payment.AuthorizedAt != null && o.Payment.AuthorizedAt >= from && o.Payment.AuthorizedAt <= to) ||
                (o.Payment.CapturedAt != null && o.Payment.CapturedAt >= from && o.Payment.CapturedAt <= to))
            .Include(o => o.Refunds)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered);
    }
}
