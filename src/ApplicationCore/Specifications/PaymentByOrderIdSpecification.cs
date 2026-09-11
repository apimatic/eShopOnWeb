using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PaymentByOrderIdSpecification : Specification<Payment>
{
    public PaymentByOrderIdSpecification(int orderId)
    {
        Query.Where(p => p.OrderId == orderId)
            .Include(p => p.Refunds);
    }
}
