using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All payments that reached PayPal (have a PayPal order id), for reconciliation.</summary>
public class OrderPaymentsWithRefundsSpec : Specification<OrderPayment>
{
    public OrderPaymentsWithRefundsSpec()
    {
        Query
            .Where(p => p.PayPalOrderId != null)
            .Include(p => p.Refunds);
    }
}
