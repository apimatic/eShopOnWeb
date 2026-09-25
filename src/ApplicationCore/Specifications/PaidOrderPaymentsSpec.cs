using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Order payments that reached PayPal (an order id was created), for reconciliation against PayPal's records.</summary>
public class PaidOrderPaymentsSpec : Specification<OrderPayment>
{
    public PaidOrderPaymentsSpec()
    {
        Query.Where(p => p.PayPalOrderId != null)
            .Include(p => p.Refunds);
    }
}
