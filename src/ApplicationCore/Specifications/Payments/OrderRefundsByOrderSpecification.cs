using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications.Payments;

public class OrderRefundsByOrderSpecification : Specification<OrderRefund>
{
    public OrderRefundsByOrderSpecification(int orderId)
    {
        Query.Where(r => r.OrderId == orderId)
            .OrderBy(r => r.CreatedAt);
    }
}
