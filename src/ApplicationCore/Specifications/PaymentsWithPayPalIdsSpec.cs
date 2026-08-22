using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PaymentsWithPayPalIdsSpec : Specification<OrderPayment>
{
    public PaymentsWithPayPalIdsSpec()
    {
        Query.Where(p => p.PayPalOrderId != null || p.AuthorizationId != null || p.CaptureId != null);
        Query.Include(p => p.Refunds);
    }
}
