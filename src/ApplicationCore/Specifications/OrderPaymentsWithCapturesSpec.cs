using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderPaymentsWithCapturesSpec : Specification<OrderPayment>
{
    public OrderPaymentsWithCapturesSpec()
    {
        Query
            .Where(p => p.CaptureId != null)
            .Include(p => p.Refunds);
    }
}
