using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderPaymentsByOrderIdsSpec : Specification<OrderPayment>
{
    public OrderPaymentsByOrderIdsSpec(IEnumerable<int> orderIds)
    {
        Query.Where(p => orderIds.Contains(p.OrderId))
            .Include(p => p.Refunds);
    }
}

public class OrderPaymentsInDateRangeSpec : Specification<OrderPayment>
{
    public OrderPaymentsInDateRangeSpec(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(p =>
                (p.AuthorizedAt != null && p.AuthorizedAt >= from && p.AuthorizedAt <= to) ||
                (p.CapturedAt != null && p.CapturedAt >= from && p.CapturedAt <= to) ||
                (p.VoidedAt != null && p.VoidedAt >= from && p.VoidedAt <= to))
            .Include(p => p.Refunds);
    }
}

public class OrdersInDateRangeSpec : Specification<Order>
{
    public OrdersInDateRangeSpec(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(o => o.OrderDate >= from && o.OrderDate <= to)
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.ItemOrdered);
    }
}
