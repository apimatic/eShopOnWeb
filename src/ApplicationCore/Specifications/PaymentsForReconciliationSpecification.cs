using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Payments that carry a PayPal invoice id and were created within the reconciliation range.
/// These are the eShop side of the reconciliation, matched against PayPal's transaction records.
/// </summary>
public class PaymentsForReconciliationSpecification : Specification<Payment>
{
    public PaymentsForReconciliationSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query
            .Where(p => p.InvoiceId != null && p.CreatedDate >= from && p.CreatedDate <= to)
            .Include(p => p.Refunds);
    }
}
