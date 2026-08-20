using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PaymentByOrderIdSpec : Specification<Payment>, ISingleResultSpecification<Payment>
{
    public PaymentByOrderIdSpec(int orderId)
    {
        Query.Where(payment => payment.OrderId == orderId);
    }
}
