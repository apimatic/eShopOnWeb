using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Payments whose money movement (creation, capture, or a refund) falls within the range,
/// used to line eShop's records up against PayPal's transaction report for reconciliation.
/// </summary>
public class PaymentsInDateRangeSpecification : Specification<Payment>
{
    public PaymentsInDateRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query
            .Where(p =>
                (p.CreatedAt >= from && p.CreatedAt <= to) ||
                (p.CapturedAt != null && p.CapturedAt >= from && p.CapturedAt <= to))
            .Include(p => p.Refunds);
    }
}
