using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class RefundsByOrderSpecification : Specification<Refund>
{
    public RefundsByOrderSpecification(int orderId)
    {
        Query.Where(r => r.OrderId == orderId)
            .OrderBy(r => r.CreatedAt);
    }
}
