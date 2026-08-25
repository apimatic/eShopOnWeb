using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PaymentWithRefundsByOrderIdSpec : Specification<Payment>
{
    public PaymentWithRefundsByOrderIdSpec(int orderId)
    {
        Query.Where(p => p.EShopOrderId == orderId)
             .Include(p => p.Refunds);
    }
}
