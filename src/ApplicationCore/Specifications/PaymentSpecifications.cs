using System;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PaymentByOrderIdSpecification : Specification<Payment>
{
    public PaymentByOrderIdSpecification(int orderId)
    {
        Query.Where(p => p.OrderId == orderId)
            .Include(p => p.Refunds);
    }
}

public class PaymentsByBuyerIdSpecification : Specification<Payment>
{
    public PaymentsByBuyerIdSpecification(string buyerId)
    {
        Query.Where(p => p.BuyerId == buyerId)
            .Include(p => p.Refunds);
    }
}

/// <summary>
/// Payments whose own activity (creation or any refund) falls inside the
/// reconciliation window.
/// </summary>
public class PaymentsInRangeSpecification : Specification<Payment>
{
    public PaymentsInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(p => (p.CreatedAt >= from && p.CreatedAt <= to)
                         || p.Refunds.Any(r => r.CreatedAt >= from && r.CreatedAt <= to))
            .Include(p => p.Refunds);
    }
}
