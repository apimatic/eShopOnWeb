using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Loads every payment that has reached PayPal (has a PayPal order id), including refunds.
/// Used by reconciliation to line eShop's records up against PayPal's own.
/// </summary>
public sealed class PaymentsWithPayPalActivitySpec : Specification<Payment>
{
    public PaymentsWithPayPalActivitySpec()
    {
        Query.Where(p => p.PayPalOrderId != null)
            .Include(p => p.Refunds);
    }
}
