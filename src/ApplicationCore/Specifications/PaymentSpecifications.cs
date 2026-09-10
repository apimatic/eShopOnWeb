using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>The payment for a single order, with its refunds loaded.</summary>
public sealed class OrderPaymentByOrderIdSpec : Specification<OrderPayment>
{
    public OrderPaymentByOrderIdSpec(int orderId)
    {
        Query.Where(p => p.OrderId == orderId)
            .Include(p => p.Refunds);
    }
}

/// <summary>All of a buyer's payments, with refunds loaded.</summary>
public sealed class OrderPaymentsByBuyerSpec : Specification<OrderPayment>
{
    public OrderPaymentsByBuyerSpec(string buyerId)
    {
        Query.Where(p => p.BuyerId == buyerId)
            .Include(p => p.Refunds);
    }
}

/// <summary>All payments that have a capture, for reconciliation against PayPal's records.</summary>
public sealed class CapturedOrderPaymentsSpec : Specification<OrderPayment>
{
    public CapturedOrderPaymentsSpec()
    {
        Query.Where(p => p.CaptureId != null)
            .Include(p => p.Refunds);
    }
}

/// <summary>A buyer's saved cards, newest first.</summary>
public sealed class SavedCardsByBuyerSpec : Specification<SavedCard>
{
    public SavedCardsByBuyerSpec(string buyerId)
    {
        Query.Where(c => c.BuyerId == buyerId)
            .OrderByDescending(c => c.CreatedAt);
    }
}

/// <summary>A buyer's most recent saved card (to reuse its PayPal customer id).</summary>
public sealed class FirstSavedCardByBuyerSpec : Specification<SavedCard>
{
    public FirstSavedCardByBuyerSpec(string buyerId)
    {
        Query.Where(c => c.BuyerId == buyerId && c.PayPalCustomerId != null)
            .OrderByDescending(c => c.CreatedAt);
    }
}
