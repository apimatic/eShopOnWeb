using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Payments whose PayPal-reported transaction time falls in a range. Reconciliation filters the
/// local side on the same PayPal clock as the PayPal side, not on the local row-creation time.
/// </summary>
public class ReconcilablePaymentsSpec : Specification<OrderPayment>
{
    public ReconcilablePaymentsSpec(DateTimeOffset from, DateTimeOffset to)
    {
        Query
            .Where(p => p.PayPalTransactionTime != null
                        && p.PayPalTransactionTime >= from
                        && p.PayPalTransactionTime <= to)
            .Include(p => p.Refunds);
    }
}
