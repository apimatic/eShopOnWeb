using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.Payment;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderPaymentByOrderIdSpec : Specification<OrderPayment>, ISingleResultSpecification<OrderPayment>
{
    public OrderPaymentByOrderIdSpec(int orderId)
    {
        Query.Where(p => p.OrderId == orderId)
             .Include(p => p.Refunds);
    }
}
