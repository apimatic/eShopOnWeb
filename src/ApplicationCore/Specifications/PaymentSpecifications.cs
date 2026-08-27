using System;
using System.Collections.Generic;
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

public class PaymentsByOrderIdsSpec : Specification<Payment>
{
    public PaymentsByOrderIdsSpec(IEnumerable<int> orderIds)
    {
        Query.Where(p => orderIds.Contains(p.OrderId))
            .Include(p => p.Refunds);
    }
}

public class PaymentByRefundIdempotencyKeySpec : Specification<Payment>
{
    public PaymentByRefundIdempotencyKeySpec(string idempotencyKey)
    {
        Query.Where(p => p.Refunds.Any(r => r.IdempotencyKey == idempotencyKey))
            .Include(p => p.Refunds);
    }
}

public class PaymentsCreatedInRangeSpec : Specification<Payment>
{
    public PaymentsCreatedInRangeSpec(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(p => p.CreatedAt >= from && p.CreatedAt <= to)
            .Include(p => p.Refunds);
    }
}
