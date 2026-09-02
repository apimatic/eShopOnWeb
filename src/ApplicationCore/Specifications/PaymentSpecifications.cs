using System;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PaymentByOrderIdSpecification : Specification<OrderPayment>
{
    public PaymentByOrderIdSpecification(int orderId)
    {
        Query.Where(p => p.OrderId == orderId)
            .Include(p => p.Refunds);
    }
}

public class PaymentByRefundIdempotencyKeySpecification : Specification<OrderPayment>
{
    public PaymentByRefundIdempotencyKeySpecification(string idempotencyKey)
    {
        Query.Where(p => p.Refunds.Any(r => r.IdempotencyKey == idempotencyKey))
            .Include(p => p.Refunds);
    }
}

public class PaymentsForReconciliationSpecification : Specification<OrderPayment>
{
    public PaymentsForReconciliationSpecification(DateTimeOffset createdUpTo)
    {
        Query.Where(p => p.CreatedAt <= createdUpTo)
            .Include(p => p.Refunds);
    }
}

public class PaymentsForBuyerSpecification : Specification<OrderPayment>
{
    public PaymentsForBuyerSpecification(string buyerId)
    {
        Query.Where(p => p.BuyerId == buyerId)
            .Include(p => p.Refunds);
    }
}

public class SavedCardsByBuyerSpecification : Specification<SavedCard>
{
    public SavedCardsByBuyerSpecification(string buyerId)
    {
        Query.Where(c => c.BuyerId == buyerId)
            .OrderByDescending(c => c.CreatedAt);
    }
}

public class SavedCardByIdSpecification : Specification<SavedCard>
{
    public SavedCardByIdSpecification(int savedCardId)
    {
        Query.Where(c => c.Id == savedCardId);
    }
}
