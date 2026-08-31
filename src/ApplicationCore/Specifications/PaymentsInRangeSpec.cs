using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Payments whose activity (creation or capture) falls inside the range, used to
/// line eShop payments up against PayPal's transaction report.
/// </summary>
public class PaymentsInRangeSpec : Specification<Payment>
{
    public PaymentsInRangeSpec(DateTimeOffset from, DateTimeOffset to)
    {
        Query
            .Where(p => (p.CreatedAt >= from && p.CreatedAt <= to)
                        || (p.CapturedAt != null && p.CapturedAt >= from && p.CapturedAt <= to))
            .Include(p => p.Refunds);
    }
}
