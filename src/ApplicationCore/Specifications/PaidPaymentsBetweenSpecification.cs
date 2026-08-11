using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Payments that reached PayPal (a PayPal order was created for them) within a date range. Used by the
/// reconciliation report to line the eShop side up against PayPal's transaction record.
/// </summary>
public class PaidPaymentsBetweenSpecification : Specification<Payment>
{
    public PaidPaymentsBetweenSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query
            .Where(p => p.PayPalOrderId != null && p.CreatedAt >= from && p.CreatedAt <= to)
            .Include(p => p.Refunds);
    }
}
