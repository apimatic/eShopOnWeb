using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class RefundsByOrderSpec : Specification<RefundRecord>
{
    public RefundsByOrderSpec(int orderId)
    {
        Query.Where(r => r.OrderId == orderId);
    }
}

public class RefundByIdempotencyKeySpec : Specification<RefundRecord>
{
    public RefundByIdempotencyKeySpec(int orderId, string idempotencyKey)
    {
        Query.Where(r => r.OrderId == orderId && r.IdempotencyKey == idempotencyKey);
    }
}
