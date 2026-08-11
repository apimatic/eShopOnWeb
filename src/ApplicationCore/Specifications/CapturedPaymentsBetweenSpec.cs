using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Payments captured within a date range, for reconciliation against PayPal's records.</summary>
public class CapturedPaymentsBetweenSpec : Specification<Payment>
{
    public CapturedPaymentsBetweenSpec(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(p => p.CaptureId != null && p.CapturedAt != null && p.CapturedAt >= from && p.CapturedAt <= to)
            .Include(p => p.Refunds);
    }
}
