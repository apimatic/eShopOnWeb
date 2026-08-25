using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PaymentInfoByOrderIdSpec : Specification<PaymentInfo>, ISingleResultSpecification<PaymentInfo>
{
    public PaymentInfoByOrderIdSpec(int orderId)
    {
        Query.Where(p => p.OrderId == orderId)
            .Include(p => p.Refunds);
    }
}
