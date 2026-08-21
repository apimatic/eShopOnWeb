using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the pay → fulfil → cancel/refund lifecycle and saved cards, coordinating the
/// domain (orders, payments, saved cards) with the PayPal gateway. All operations are scoped by
/// the caller's identity where the endpoint is shopper-scoped.
/// </summary>
public interface IPaymentService
{
    /// <summary>Place an order for the buyer from catalog items. Returns the created order.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, Address shipToAddress, CancellationToken ct);

    /// <summary>Authorize (hold) the order total using a one-off card or one of the buyer's saved cards. Returns the authorized order.</summary>
    Task<Order> AuthorizeOrderAsync(int orderId, string buyerId, PayPalCardData? card, int? savedPaymentMethodId, CancellationToken ct);

    /// <summary>Fulfil the order — capture the held money. Renews a stale hold rather than failing. Operator action. Returns the fulfilled order.</summary>
    Task<Order> FulfilOrderAsync(int orderId, CancellationToken ct);

    /// <summary>Cancel the order before fulfilment — release the hold. Operator action. Returns the cancelled order.</summary>
    Task<Order> CancelOrderAsync(int orderId, CancellationToken ct);

    /// <summary>Refund a captured order in full or in part. Returns the order carrying the new refund (found by idempotency key).</summary>
    Task<Order> RefundOrderAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey, CancellationToken ct);

    /// <summary>The buyer's orders with their payment state, newest first.</summary>
    Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken ct);

    /// <summary>Vault a card for the buyer. Returns the saved card (safe descriptor only).</summary>
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, PayPalCardData card, CancellationToken ct);

    /// <summary>The buyer's saved cards.</summary>
    Task<IReadOnlyList<SavedPaymentMethod>> GetCardsForBuyerAsync(string buyerId, CancellationToken ct);

    /// <summary>Delete one of the buyer's saved cards (also removes it from the PayPal vault).</summary>
    Task DeleteCardAsync(int paymentMethodId, string buyerId, CancellationToken ct);

    /// <summary>Reconcile PayPal's transactions against eShop orders over a date range. Operator action.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
