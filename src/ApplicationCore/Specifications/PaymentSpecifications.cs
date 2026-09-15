using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>The payment for a single order, with its refunds loaded.</summary>
public class OrderPaymentByOrderIdSpec : Specification<OrderPayment>
{
    public OrderPaymentByOrderIdSpec(int orderId)
    {
        Query
            .Where(p => p.OrderId == orderId)
            .Include(p => p.Refunds);
    }
}

/// <summary>All of a shopper's payments (their orders), with refunds, newest first.</summary>
public class OrderPaymentsByBuyerSpec : Specification<OrderPayment>
{
    public OrderPaymentsByBuyerSpec(string buyerId)
    {
        Query
            .Where(p => p.BuyerId == buyerId)
            .Include(p => p.Refunds)
            .OrderByDescending(p => p.CreatedAt);
    }
}

/// <summary>Payments whose PayPal invoice id is in the given set — used to reconcile against PayPal records.</summary>
public class OrderPaymentsByInvoiceIdsSpec : Specification<OrderPayment>
{
    public OrderPaymentsByInvoiceIdsSpec(string[] invoiceIds)
    {
        Query
            .Where(p => invoiceIds.Contains(p.InvoiceId))
            .Include(p => p.Refunds);
    }
}

/// <summary>All payments that reached PayPal (have a PayPal order id) — the eShop side of reconciliation.</summary>
public class PaidOrderPaymentsSpec : Specification<OrderPayment>
{
    public PaidOrderPaymentsSpec()
    {
        Query
            .Where(p => p.PayPalOrderId != null)
            .Include(p => p.Refunds);
    }
}

/// <summary>A shopper's saved cards, newest first.</summary>
public class SavedPaymentMethodsByBuyerSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodsByBuyerSpec(string buyerId)
    {
        Query
            .Where(m => m.BuyerId == buyerId)
            .OrderByDescending(m => m.CreatedAt);
    }
}
