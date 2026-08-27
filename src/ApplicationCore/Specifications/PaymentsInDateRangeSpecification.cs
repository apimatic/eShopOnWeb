using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Payments whose money movement (authorization or capture) falls inside the range,
/// for reconciliation against PayPal's transaction report.
/// </summary>
public class PaymentsInDateRangeSpecification : Specification<Payment>
{
    public PaymentsInDateRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(p =>
                (p.CreatedAt >= from && p.CreatedAt <= to) ||
                (p.CapturedAt != null && p.CapturedAt >= from && p.CapturedAt <= to))
            .Include(p => p.Refunds);
    }
}
