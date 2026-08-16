using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Payments whose capture (money movement) occurred within the given range — used by reconciliation.</summary>
public class CapturedOrderPaymentsInRangeSpecification : Specification<OrderPayment>
{
    public CapturedOrderPaymentsInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query
            .Where(p => p.CapturedAt != null && p.CapturedAt >= from && p.CapturedAt <= to)
            .Include(p => p.Refunds);
    }
}
