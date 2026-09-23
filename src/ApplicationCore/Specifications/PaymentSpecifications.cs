using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>One payment for a given order, with its refunds loaded.</summary>
public class OrderPaymentByOrderIdSpec : Specification<OrderPayment>, ISingleResultSpecification<OrderPayment>
{
    public OrderPaymentByOrderIdSpec(int orderId)
    {
        Query.Where(p => p.OrderId == orderId)
            .Include(p => p.Refunds);
    }
}

/// <summary>All of a buyer's payments, with refunds, newest order first.</summary>
public class OrderPaymentsByBuyerSpec : Specification<OrderPayment>
{
    public OrderPaymentsByBuyerSpec(string buyerId)
    {
        Query.Where(p => p.BuyerId == buyerId)
            .Include(p => p.Refunds)
            .OrderByDescending(p => p.OrderId);
    }
}

/// <summary>
/// Payments whose PayPal event time falls in the range — the same clock reconciliation filters PayPal on.
/// </summary>
public class OrderPaymentsByPayPalTimeSpec : Specification<OrderPayment>
{
    public OrderPaymentsByPayPalTimeSpec(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(p => p.PayPalCreatedAt != null
                      && p.PayPalCreatedAt >= from
                      && p.PayPalCreatedAt <= to)
            .Include(p => p.Refunds);
    }
}
