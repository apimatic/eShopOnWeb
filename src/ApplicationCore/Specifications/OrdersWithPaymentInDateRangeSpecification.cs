using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Orders whose payment activity (capture, or authorization if not yet captured) falls in [from, to).</summary>
public class OrdersWithPaymentInDateRangeSpecification : Specification<Order>
{
    public OrdersWithPaymentInDateRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query
            .Where(o => o.Payment != null &&
                ((o.Payment.CapturedAt != null && o.Payment.CapturedAt >= from && o.Payment.CapturedAt < to) ||
                 (o.Payment.CapturedAt == null && o.OrderDate >= from && o.OrderDate < to)))
            .Include(o => o.Payment!)
                .ThenInclude(p => p.Refunds);
    }
}
