using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

// Orders whose payment was captured within [from, to] - the local half of a reconciliation report.
public class OrdersCapturedInRangeSpec : Specification<Order>
{
    public OrdersCapturedInRangeSpec(DateTimeOffset from, DateTimeOffset to)
    {
        Query
            .Where(order => order.Payment != null
                && order.Payment.CapturedAt != null
                && order.Payment.CapturedAt >= from
                && order.Payment.CapturedAt <= to)
            .Include(o => o.Payment)
            .ThenInclude(p => p!.Refunds);
    }
}
