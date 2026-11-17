using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class RefundsByPaymentIdSpecification : Specification<Refund>
{
    public RefundsByPaymentIdSpecification(int paymentId)
    {
        Query.Where(r => r.PaymentId == paymentId);
    }
}

public class RefundsByOwnerSpecification : Specification<Refund>
{
    public RefundsByOwnerSpecification(string ownerId)
    {
        Query.Where(r => r.OwnerId == ownerId);
    }
}

public class RefundsByOrderIdsSpecification : Specification<Refund>
{
    public RefundsByOrderIdsSpecification(IReadOnlyCollection<int> orderIds)
    {
        Query.Where(r => orderIds.Contains(r.OrderId));
    }
}

public class RefundByOrderAndIdempotencyKeySpecification : Specification<Refund>
{
    public RefundByOrderAndIdempotencyKeySpecification(int orderId, string idempotencyKey)
    {
        Query.Where(r => r.OrderId == orderId && r.IdempotencyKey == idempotencyKey);
    }
}
