using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Payments created within a date range that reached PayPal (have a PayPal order id). Used by
/// reconciliation to find eShop-side payments to line up against PayPal's transaction report.
/// </summary>
public class PaymentsWithinRangeSpecification : Specification<Payment>
{
    public PaymentsWithinRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(p => p.CreatedAt >= from && p.CreatedAt <= to && p.PayPalOrderId != null)
            .Include(p => p.Refunds);
    }
}
