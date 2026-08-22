using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrdersForReconciliationSpec : Specification<Order>
{
    public OrdersForReconciliationSpec(DateTimeOffset from, DateTimeOffset to)
    {
        Query
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.ItemOrdered)
            .Include(o => o.Refunds)
            .Where(o =>
                (o.OrderDate >= from && o.OrderDate <= to) ||
                (o.AuthorizedAt != null && o.AuthorizedAt >= from && o.AuthorizedAt <= to) ||
                (o.CapturedAt != null && o.CapturedAt >= from && o.CapturedAt <= to));
    }
}
