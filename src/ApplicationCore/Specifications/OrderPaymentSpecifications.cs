using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>One caller's payment for a specific order, with its refunds. Scoped to the buyer.</summary>
public class OrderPaymentByOrderIdSpecification : Specification<OrderPayment>, ISingleResultSpecification<OrderPayment>
{
    public OrderPaymentByOrderIdSpecification(int orderId, string buyerId)
    {
        Query.Where(p => p.OrderId == orderId && p.BuyerId == buyerId)
            .Include(p => p.Refunds);
    }
}

/// <summary>A payment by order id regardless of owner — for reconciliation (operator scope only).</summary>
public class OrderPaymentByOrderIdAnyOwnerSpecification : Specification<OrderPayment>, ISingleResultSpecification<OrderPayment>
{
    public OrderPaymentByOrderIdAnyOwnerSpecification(int orderId)
    {
        Query.Where(p => p.OrderId == orderId)
            .Include(p => p.Refunds);
    }
}

/// <summary>All of one caller's payments, newest first, with refunds.</summary>
public class OrderPaymentsByBuyerSpecification : Specification<OrderPayment>
{
    public OrderPaymentsByBuyerSpecification(string buyerId)
    {
        Query.Where(p => p.BuyerId == buyerId)
            .Include(p => p.Refunds)
            .OrderByDescending(p => p.CreatedAt);
    }
}

/// <summary>
/// Payments whose PayPal money movement (authorization/capture) happened within a window — used to line
/// eShop's own records up against PayPal's transaction report over the same clock. Operator scope.
/// </summary>
public class OrderPaymentsInPayPalWindowSpecification : Specification<OrderPayment>
{
    public OrderPaymentsInPayPalWindowSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(p =>
                (p.AuthorizedAt != null && p.AuthorizedAt >= from && p.AuthorizedAt <= to) ||
                (p.CapturedAt != null && p.CapturedAt >= from && p.CapturedAt <= to))
            .Include(p => p.Refunds);
    }
}
