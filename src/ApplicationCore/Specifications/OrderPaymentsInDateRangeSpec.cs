using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Payments authorized or captured within the given range, for reconciliation.</summary>
public class OrderPaymentsInDateRangeSpec : Specification<OrderPayment>
{
    public OrderPaymentsInDateRangeSpec(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(p =>
            (p.CreatedAt >= from && p.CreatedAt <= to) ||
            (p.CapturedAt.HasValue && p.CapturedAt >= from && p.CapturedAt <= to));
    }
}
