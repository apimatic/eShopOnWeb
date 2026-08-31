using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PaymentByOrderIdSpec : Specification<Payment>
{
    public PaymentByOrderIdSpec(int orderId)
    {
        Query.Where(p => p.OrderId == orderId)
            .Include(p => p.Refunds);
    }
}

public class PaymentsByBuyerSpec : Specification<Payment>
{
    public PaymentsByBuyerSpec(string buyerId)
    {
        Query.Where(p => p.BuyerId == buyerId)
            .Include(p => p.Refunds);
    }
}

public class PaymentsCreatedBetweenSpec : Specification<Payment>
{
    public PaymentsCreatedBetweenSpec(System.DateTimeOffset from, System.DateTimeOffset to)
    {
        Query.Where(p => p.CreatedAt >= from && p.CreatedAt <= to)
            .Include(p => p.Refunds);
    }
}

public class PaymentByRefundKeySpec : Specification<Payment>
{
    public PaymentByRefundKeySpec(string idempotencyKey)
    {
        Query.Where(p => p.Refunds.Any(r => r.IdempotencyKey == idempotencyKey))
            .Include(p => p.Refunds);
    }
}
