using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>The payment for an order (operator scope), with its refunds loaded.</summary>
public class PaymentByOrderSpecification : Specification<Payment>
{
    public PaymentByOrderSpecification(int orderId)
    {
        Query.Where(p => p.OrderId == orderId)
            .Include(p => p.Refunds);
    }
}
