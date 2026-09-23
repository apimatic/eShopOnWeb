using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Payments whose PayPal event time (<see cref="OrderPayment.ProcessedAt"/>) falls within a range — the
/// same clock the PayPal transaction search filters on, so both sides of reconciliation line up.
/// </summary>
public class OrderPaymentsByProcessedRangeSpecification : Specification<OrderPayment>
{
    public OrderPaymentsByProcessedRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(p => p.ProcessedAt != null && p.ProcessedAt >= from && p.ProcessedAt <= to)
            .Include(p => p.Refunds);
    }
}
