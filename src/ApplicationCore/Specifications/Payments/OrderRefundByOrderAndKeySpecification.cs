using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications.Payments;

/// <summary>Finds an existing refund for (order, idempotency key) so a repeated request returns the same refund.</summary>
public class OrderRefundByOrderAndKeySpecification : Specification<OrderRefund>
{
    public OrderRefundByOrderAndKeySpecification(int orderId, string idempotencyKey)
    {
        Query.Where(r => r.OrderId == orderId && r.IdempotencyKey == idempotencyKey);
    }
}
