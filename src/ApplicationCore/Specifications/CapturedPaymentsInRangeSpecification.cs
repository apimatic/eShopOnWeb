using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Payments whose capture (money movement) falls within a date range — the eShop side of reconciliation,
/// filtered on the same money-movement clock the PayPal transaction report uses, not a row-creation column.
/// </summary>
public sealed class CapturedPaymentsInRangeSpecification : Specification<OrderPayment>
{
    public CapturedPaymentsInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(p => p.CaptureId != null && p.CapturedAt != null
                && p.CapturedAt >= from && p.CapturedAt <= to)
            .Include(p => p.Refunds);
    }
}
