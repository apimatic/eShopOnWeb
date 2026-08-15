using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Payments created within a date range (used to build the reconciliation report).</summary>
public class PaymentsInRangeSpecification : Specification<Payment>
{
    public PaymentsInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query
            .Where(p => p.CreatedAt >= from && p.CreatedAt <= to)
            .Include(p => p.Refunds);
    }
}
