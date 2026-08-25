using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderRefundByKeySpec : Specification<OrderRefund>
{
    public OrderRefundByKeySpec(int orderId, string idempotencyKey)
    {
        Query.Where(r => r.OrderId == orderId && r.IdempotencyKey == idempotencyKey);
    }
}
