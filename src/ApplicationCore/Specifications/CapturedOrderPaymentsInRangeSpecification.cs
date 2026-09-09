using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>eShop-side leg of reconciliation: payments captured within a date range.</summary>
public class CapturedOrderPaymentsInRangeSpecification : Specification<OrderPayment>
{
    public CapturedOrderPaymentsInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(p => p.CaptureId != null && p.CapturedAt != null
                         && p.CapturedAt >= from && p.CapturedAt <= to)
            .Include(p => p.Refunds);
    }
}
