using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>The payment for one order, with its refunds.</summary>
public class PaymentByOrderIdSpecification : Specification<OrderPayment>
{
    public PaymentByOrderIdSpecification(int orderId)
    {
        Query.Where(payment => payment.OrderId == orderId)
            .Include(payment => payment.Refunds);
    }
}

/// <summary>All of a shopper's payments, newest first.</summary>
public class PaymentsForBuyerSpecification : Specification<OrderPayment>
{
    public PaymentsForBuyerSpecification(string buyerId)
    {
        Query.Where(payment => payment.BuyerId == buyerId)
            .Include(payment => payment.Refunds)
            .OrderByDescending(payment => payment.Id);
    }
}

/// <summary>Payments created, last touched or captured inside a date range, for reconciliation.</summary>
public class PaymentsActiveInRangeSpecification : Specification<OrderPayment>
{
    public PaymentsActiveInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(payment =>
                (payment.Created >= from && payment.Created <= to) ||
                (payment.Updated >= from && payment.Updated <= to) ||
                (payment.CapturedDate >= from && payment.CapturedDate <= to))
            .Include(payment => payment.Refunds);
    }
}

/// <summary>A shopper's saved cards.</summary>
public class SavedCardsForBuyerSpecification : Specification<PaymentMethod>
{
    public SavedCardsForBuyerSpecification(string buyerId)
    {
        Query.Where(card => card.BuyerId == buyerId)
            .OrderBy(card => card.Id);
    }
}
