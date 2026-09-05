using System;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>The payment attached to an order, including its refunds.</summary>
public class PaymentByOrderIdSpecification : Specification<Payment>
{
    public PaymentByOrderIdSpecification(int orderId)
    {
        Query.Where(p => p.OrderId == orderId)
            .Include(p => p.Refunds);
    }
}

/// <summary>All payments belonging to a buyer, including their refunds.</summary>
public class PaymentsForBuyerSpecification : Specification<Payment>
{
    public PaymentsForBuyerSpecification(string buyerId)
    {
        Query.Where(p => p.BuyerId == buyerId)
            .Include(p => p.Refunds);
    }
}

/// <summary>All payments captured or refunded within a date range (for reconciliation).</summary>
public class PaymentsWithEventsBetweenSpecification : Specification<Payment>
{
    public PaymentsWithEventsBetweenSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(p =>
                (p.CapturedAt != null && p.CapturedAt >= from && p.CapturedAt <= to) ||
                p.Refunds.Any(r => r.CreatedAt >= from && r.CreatedAt <= to))
            .Include(p => p.Refunds);
    }
}

/// <summary>The saved cards belonging to a buyer, newest first.</summary>
public class SavedCardsForBuyerSpecification : Specification<SavedCard>
{
    public SavedCardsForBuyerSpecification(string buyerId)
    {
        Query.Where(c => c.BuyerId == buyerId)
            .OrderByDescending(c => c.CreatedAt);
    }
}
