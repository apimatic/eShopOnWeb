using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrdersWithPaymentsInRangeSpec : Specification<Order>
{
    public OrdersWithPaymentsInRangeSpec(DateTimeOffset from, DateTimeOffset to)
    {
        Query
            .Where(o =>
                o.PayPalOrderId != null
                || (o.OrderDate >= from && o.OrderDate <= to))
            .Include(o => o.Refunds);
    }
}
