using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderPaymentByOrderIdSpec : Specification<OrderPayment>, ISingleResultSpecification<OrderPayment>
{
    public OrderPaymentByOrderIdSpec(int orderId)
    {
        Query.Where(p => p.OrderId == orderId)
             .Include(nameof(OrderPayment.Refunds));
    }
}

public class AllOrderPaymentsWithRefundsSpec : Specification<OrderPayment>
{
    public AllOrderPaymentsWithRefundsSpec()
    {
        Query.Include(nameof(OrderPayment.Refunds));
    }
}
