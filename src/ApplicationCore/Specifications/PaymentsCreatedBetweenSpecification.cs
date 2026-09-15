using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Payments that saw money movement within a window, for lining up against PayPal's own record.
/// Uses the fulfilment/creation timestamps so the eShop side of reconciliation covers the same range.
/// </summary>
public class PaymentsCreatedBetweenSpecification : Specification<Payment>
{
    public PaymentsCreatedBetweenSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(p => p.CreatedAt >= from && p.CreatedAt <= to);
    }
}
