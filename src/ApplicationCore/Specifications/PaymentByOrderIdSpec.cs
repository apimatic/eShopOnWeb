using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Loads a payment (with its refunds) for a given order.</summary>
public class PaymentByOrderIdSpec : Specification<Payment>, ISingleResultSpecification<Payment>
{
    public PaymentByOrderIdSpec(int orderId)
    {
        Query
            .Where(p => p.OrderId == orderId)
            .Include(p => p.Refunds);
    }
}
