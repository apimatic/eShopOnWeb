using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the pay-for-an-order and saved-card flows on top of the reusable Order model and
/// the PayPal gateway. Shopper-scoped methods take the caller's buyer id and act only on that
/// shopper's data; operator methods act on any order (endpoint auth restricts them to admins).
/// </summary>
public interface IPaymentService
{
    // ---- Flow 1: pay for an order ----

    /// <summary>Places an order from catalog items, awaiting payment. Shopper-scoped.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines, Address shipToAddress);

    /// <summary>Authorizes (holds) the order total. Shopper-scoped; idempotent in effect.</summary>
    Task<OrderPaymentView> AuthorizeOrderAsync(string buyerId, int orderId, PayInstruction instruction);

    /// <summary>Fulfils the order and captures the held funds. Operator action; idempotent in effect.</summary>
    Task<OrderPaymentView> FulfilOrderAsync(int orderId);

    /// <summary>Cancels before fulfilment, releasing the hold. Operator action; idempotent in effect.</summary>
    Task<OrderPaymentView> CancelOrderAsync(int orderId);

    /// <summary>Refunds a captured payment in full or in part. Operator action; idempotent per key.</summary>
    Task<(PaymentRefund Refund, OrderPaymentView View)> RefundOrderAsync(int orderId, decimal? amount, string idempotencyKey);

    /// <summary>The caller's orders with their payment state. Shopper-scoped.</summary>
    Task<IReadOnlyList<OrderPaymentView>> GetMyOrdersAsync(string buyerId);

    // ---- Flow 2: saved cards ----

    /// <summary>Vaults a card for the shopper. Shopper-scoped.</summary>
    Task<SavedCard> SaveCardAsync(string buyerId, GatewayCardDetails card);

    /// <summary>The caller's saved cards. Shopper-scoped.</summary>
    Task<IReadOnlyList<SavedCard>> ListCardsAsync(string buyerId);

    /// <summary>Removes one of the caller's saved cards. Shopper-scoped.</summary>
    Task DeleteCardAsync(string buyerId, int paymentMethodId);

    // ---- Reconciliation (operator) ----

    /// <summary>Lines up PayPal's transaction record against eShop orders over a date range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to);
}
