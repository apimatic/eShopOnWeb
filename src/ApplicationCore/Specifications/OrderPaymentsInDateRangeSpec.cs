using System;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Payments with any payment activity inside the range (authorized, captured or refunded).
/// </summary>
public class OrderPaymentsInDateRangeSpec : Specification<OrderPayment>
{
    public OrderPaymentsInDateRangeSpec(DateTimeOffset from, DateTimeOffset to)
    {
        Query
            .Where(p => (p.CreatedAt >= from && p.CreatedAt <= to)
                        || (p.CapturedAt != null && p.CapturedAt >= from && p.CapturedAt <= to)
                        || p.Refunds.Any(r => r.CreatedAt >= from && r.CreatedAt <= to))
            .Include(p => p.Refunds);
    }
}
