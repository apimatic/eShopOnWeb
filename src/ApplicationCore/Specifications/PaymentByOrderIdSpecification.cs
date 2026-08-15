using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>The payment for a given order, with its refunds loaded.</summary>
public class PaymentByOrderIdSpecification : Specification<Payment>, ISingleResultSpecification<Payment>
{
    public PaymentByOrderIdSpecification(int orderId)
    {
        Query
            .Where(p => p.OrderId == orderId)
            .Include(p => p.Refunds);
    }
}
