using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>The single payment for an order, with its refund ledger.</summary>
public sealed class OrderPaymentByOrderIdSpec : Specification<OrderPayment>, ISingleResultSpecification<OrderPayment>
{
    public OrderPaymentByOrderIdSpec(int orderId)
    {
        Query.Where(p => p.OrderId == orderId)
            .Include(p => p.Refunds);
    }
}

/// <summary>The payment for an order, scoped to its owner (a shopper never sees another's).</summary>
public sealed class OrderPaymentByOrderIdForBuyerSpec : Specification<OrderPayment>, ISingleResultSpecification<OrderPayment>
{
    public OrderPaymentByOrderIdForBuyerSpec(int orderId, string buyerId)
    {
        Query.Where(p => p.OrderId == orderId && p.BuyerId == buyerId)
            .Include(p => p.Refunds);
    }
}

/// <summary>All of a shopper's payments with their refund ledgers.</summary>
public sealed class OrderPaymentsByBuyerSpec : Specification<OrderPayment>
{
    public OrderPaymentsByBuyerSpec(string buyerId)
    {
        Query.Where(p => p.BuyerId == buyerId)
            .Include(p => p.Refunds);
    }
}

/// <summary>
/// Payments whose PayPal event time (authorization/capture) falls in a range — used by reconciliation so
/// the local side filters on the same clock the PayPal report does, never on a local row-creation column.
/// </summary>
public sealed class OrderPaymentsInEventWindowSpec : Specification<OrderPayment>
{
    public OrderPaymentsInEventWindowSpec(DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        Query.Where(p =>
                (p.AuthorizedAtUtc != null && p.AuthorizedAtUtc >= fromUtc && p.AuthorizedAtUtc <= toUtc) ||
                (p.CapturedAtUtc != null && p.CapturedAtUtc >= fromUtc && p.CapturedAtUtc <= toUtc))
            .Include(p => p.Refunds);
    }
}

/// <summary>All cards a shopper has saved.</summary>
public sealed class SavedPaymentMethodsByBuyerSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodsByBuyerSpec(string buyerId)
    {
        Query.Where(m => m.BuyerId == buyerId);
    }
}

/// <summary>A single saved card, scoped to its owner (a shopper never uses or deletes another's).</summary>
public sealed class SavedPaymentMethodByIdForBuyerSpec : Specification<SavedPaymentMethod>, ISingleResultSpecification<SavedPaymentMethod>
{
    public SavedPaymentMethodByIdForBuyerSpec(int id, string buyerId)
    {
        Query.Where(m => m.Id == id && m.BuyerId == buyerId);
    }
}

/// <summary>The most recent saved card for a buyer, used to reuse the PayPal customer id on later saves.</summary>
public sealed class LatestSavedPaymentMethodForBuyerSpec : Specification<SavedPaymentMethod>, ISingleResultSpecification<SavedPaymentMethod>
{
    public LatestSavedPaymentMethodForBuyerSpec(string buyerId)
    {
        Query.Where(m => m.BuyerId == buyerId && m.PayPalCustomerId != null)
            .OrderByDescending(m => m.CreatedAtUtc);
    }
}
