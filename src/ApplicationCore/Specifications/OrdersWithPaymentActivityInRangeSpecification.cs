using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Orders whose payment had authorization or capture activity within [from, to] - the local side of a
/// reconciliation report.
/// </summary>
public class OrdersWithPaymentActivityInRangeSpecification : Specification<Order>
{
    public OrdersWithPaymentActivityInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query
            .Where(o => o.Payment != null &&
                        ((o.Payment.AuthorizedAt != null && o.Payment.AuthorizedAt >= from && o.Payment.AuthorizedAt <= to) ||
                         (o.Payment.CapturedAt != null && o.Payment.CapturedAt >= from && o.Payment.CapturedAt <= to)))
            .Include(o => o.Payment)
                .ThenInclude(p => p!.Refunds);
    }
}
